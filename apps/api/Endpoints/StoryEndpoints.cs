using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using StoryWalker.Application.Agent;
using StoryWalker.Application.Api;
using StoryWalker.Application.Brief;
using StoryWalker.Application.Messaging;
using StoryWalker.Domain.Entities;
using StoryWalker.Domain.Enums;
using StoryWalker.Infrastructure.Persistence;

namespace StoryWalker.Api.Endpoints;

/// <summary>
/// Story endpoints backing the dashboard list and the story viewer. Maps domain entities to the
/// wire DTOs in <see cref="StoryWalker.Application.Api"/> (status as string).
/// </summary>
public static class StoryEndpoints
{
    /// <summary>Shared projection so the list/detail/patch handlers map a <see cref="Story"/> the same way.</summary>
    private static readonly Expression<Func<Story, StoryDto>> ToStoryDto = s => new StoryDto(
        s.Id,
        s.Title,
        s.Summary,
        s.Status.ToString(),
        s.ArtStyle,
        s.Palette,
        s.BasedOnStoryId,
        s.Pages.OrderBy(p => p.PageNo)
            .Select(p => new PageDto(p.PageNo, p.Content, p.ImageUrl, p.Status.ToString()))
            .ToList(),
        s.Pages.SelectMany(p => p.CharacterPages)
            .Select(cp => cp.Character)
            .Distinct()
            .Select(c => new CharacterDto(c.Id, c.Name, c.Description, c.ReferenceImageUrl))
            .ToList()
    );

    public static IEndpointRouteBuilder MapStoryEndpoints(this IEndpointRouteBuilder app)
    {
        var stories = app.MapGroup("/stories").WithTags("Stories");

        stories
            .MapPost(
                "/conversations",
                async (
                    AgentMessageRequest request,
                    IStoryAgent agent,
                    StoryWalkerDbContext db,
                    IBriefPublisher publisher,
                    CancellationToken ct
                ) =>
                {
                    if (string.IsNullOrWhiteSpace(request.Message))
                    {
                        return Results.ValidationProblem(
                            new Dictionary<string, string[]>
                            {
                                ["message"] = ["Message is required."],
                            }
                        );
                    }

                    // ConversationId is null on the first turn; the agent issues one and we echo it
                    // back so the client can continue the conversation. An unknown id (e.g. after a
                    // conversation was purged) is a 404 so the client starts a fresh conversation.
                    try
                    {
                        var turn = await agent.SendAsync(
                            request.ConversationId,
                            request.Message.Trim(),
                            ct
                        );

                        // Brief authored: persist the story (status=Generating) and enqueue the brief
                        // for the generation worker, then return 202 (ADR-0003). Otherwise it's an
                        // ordinary conversational turn — echo the reply with 200.
                        if (turn is { BriefReady: true, Brief: not null })
                        {
                            var story = await StartGenerationAsync(db, publisher, turn.Brief, ct);
                            return Results.Accepted(
                                $"/stories/{story.Id}",
                                new StoryQueuedResponse(story.Id, story.Status.ToString())
                            );
                        }

                        return Results.Ok(
                            new AgentMessageResponse(
                                turn.ConversationId,
                                turn.Reply,
                                turn.BriefReady
                            )
                        );
                    }
                    catch (ConversationNotFoundException)
                    {
                        return Results.NotFound();
                    }
                    catch (BriefValidationException ex)
                    {
                        // The agent confirmed but produced an unusable brief; the turn was rolled
                        // back, so the client can resend the confirmation to author again.
                        return Results.Problem(
                            detail: ex.Message,
                            statusCode: StatusCodes.Status502BadGateway
                        );
                    }
                }
            )
            .WithName("SendAgentMessage");

        stories
            .MapGet(
                "/",
                async (StoryWalkerDbContext db) =>
                {
                    var summaries = await db
                        .Stories.OrderByDescending(s => s.CreatedAt)
                        .Select(s => new StorySummaryDto(
                            s.Id,
                            s.Title,
                            s.Status.ToString(),
                            s.CreatedAt
                        ))
                        .ToListAsync();

                    return Results.Ok(summaries);
                }
            )
            .WithName("ListStories");

        stories
            .MapGet(
                "/{id:guid}",
                async (Guid id, StoryWalkerDbContext db) =>
                {
                    var story = await db
                        .Stories.Where(s => s.Id == id)
                        .Select(ToStoryDto)
                        .FirstOrDefaultAsync();

                    return story is null ? Results.NotFound() : Results.Ok(story);
                }
            )
            .WithName("GetStory");

        stories
            .MapPatch(
                "/{id:guid}",
                async (Guid id, UpdateStoryTitleRequest request, StoryWalkerDbContext db) =>
                {
                    if (string.IsNullOrWhiteSpace(request.Title))
                    {
                        return Results.ValidationProblem(
                            new Dictionary<string, string[]> { ["title"] = ["Title is required."] }
                        );
                    }

                    var story = await db.Stories.FirstOrDefaultAsync(s => s.Id == id);
                    if (story is null)
                    {
                        return Results.NotFound();
                    }

                    story.Title = request.Title.Trim();
                    await db.SaveChangesAsync();

                    // Re-project the saved story so the client gets the updated representation.
                    var updated = await db
                        .Stories.Where(s => s.Id == id)
                        .Select(ToStoryDto)
                        .FirstAsync();

                    return Results.Ok(updated);
                }
            )
            .WithName("UpdateStoryTitle");

        stories
            .MapDelete(
                "/{id:guid}",
                async (Guid id, StoryWalkerDbContext db) =>
                {
                    var story = await db.Stories.FirstOrDefaultAsync(s => s.Id == id);
                    if (story is null)
                    {
                        return Results.NotFound();
                    }

                    db.Stories.Remove(story);

                    try
                    {
                        // Pages + CharacterPage links cascade (StoryConfiguration); user-owned Characters
                        // are kept. A story that another story is "based on" is protected by a Restrict FK.
                        await db.SaveChangesAsync();
                    }
                    catch (DbUpdateException)
                    {
                        return Results.Conflict(
                            "Story cannot be deleted because another story reuses it."
                        );
                    }

                    return Results.NoContent();
                }
            )
            .WithName("DeleteStory");

        return app;
    }

    /// <summary>
    /// Persists a <see cref="Story"/> in <see cref="StoryStatus.Generating"/> from the authored brief
    /// and enqueues that brief for the generation worker (ADR-0003). Only the story row is created
    /// here; the worker's <c>PersistBrief</c> activity writes the pages and characters.
    /// </summary>
    private static async Task<Story> StartGenerationAsync(
        StoryWalkerDbContext db,
        IBriefPublisher publisher,
        GenerationBrief brief,
        CancellationToken ct
    )
    {
        // Ownership: until Clerk auth lands (ADR-0002) there is no current user, so the story is
        // owned by the single seeded user. Fails fast if the database has no user at all.
        var userId = await db.Users.Select(u => u.Id).FirstOrDefaultAsync(ct);
        if (userId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Cannot start generation: no user exists to own the story."
            );
        }

        var story = new Story
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = brief.Story.Title,
            // The brief carries no summary yet (ADR-0013); EmbedStory will need one later.
            Summary = string.Empty,
            Status = StoryStatus.Generating,
            ArtStyle = brief.ArtStyle,
            Palette = brief.Palette,
        };

        db.Stories.Add(story);
        await db.SaveChangesAsync(ct);

        // Dual-write: the story is committed before the enqueue. A transactional outbox would make
        // this atomic (deferred); if the publish throws, the request fails and the story is left in
        // Generating for a later retry/cleanup.
        await publisher.PublishAsync(story.Id, brief, ct);

        return story;
    }
}
