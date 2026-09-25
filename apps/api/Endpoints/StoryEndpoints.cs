using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using StoryWalker.Api.Auth;
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
        s.TargetAge,
        s.Status.ToString(),
        s.ArtStyle,
        s.Pages.OrderBy(p => p.PageNo)
            .Select(p => new PageDto(p.PageNo, p.Content, p.ImageUrl, p.Status.ToString()))
            .ToList()
    );

    public static IEndpointRouteBuilder MapStoryEndpoints(this IEndpointRouteBuilder app)
    {
        var stories = app.MapGroup("/stories").WithTags("Stories").RequireAuthorization();

        stories
            .MapPost(
                "/conversations",
                async (
                    AgentMessageRequest request,
                    IStoryAgent agent,
                    StoryWalkerDbContext db,
                    IBriefPublisher publisher,
                    CurrentUser currentUser,
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
                        // Resolved on every turn, not just the brief-ready one: an interview that
                        // never finishes must still provision the user (Task 2/ADR-0002).
                        var me = await currentUser.GetIdAsync(ct);

                        var turn = await agent.SendAsync(
                            me,
                            request.ConversationId,
                            request.Message.Trim(),
                            ct
                        );

                        // Brief authored: persist the story (status=Generating) and enqueue the brief
                        // for the generation worker, then return 202 (ADR-0003). Otherwise it's an
                        // ordinary conversational turn — echo the reply with 200.
                        if (turn is { BriefReady: true, Brief: not null })
                        {
                            var story = await StartGenerationAsync(
                                db,
                                publisher,
                                me,
                                Guid.Parse(turn.ConversationId),
                                turn.Brief,
                                ct
                            );

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
                    catch (ContentFilteredException)
                    {
                        // The model's content filter refused the turn, which was rolled back, so
                        // the parent can simply send something else (ADR-0024). The UI shows the
                        // detail as-is, so it says what to do rather than what went wrong.
                        return Results.Problem(
                            detail: "Story Walker only writes stories that are right for children, so it can't help with that. Please rephrase your message and try again.",
                            statusCode: StatusCodes.Status422UnprocessableEntity
                        );
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
                async (StoryWalkerDbContext db, CurrentUser currentUser, CancellationToken ct) =>
                {
                    var me = await currentUser.GetIdAsync(ct);

                    var summaries = await db
                        .Stories.Where(s => s.UserId == me)
                        .OrderByDescending(s => s.CreatedAt)
                        .Select(s => new StorySummaryDto(
                            s.Id,
                            s.Title,
                            s.Status.ToString(),
                            s.CreatedAt
                        ))
                        .ToListAsync(ct);

                    return Results.Ok(summaries);
                }
            )
            .WithName("ListStories");

        stories
            .MapGet(
                "/{id:guid}",
                async (
                    Guid id,
                    StoryWalkerDbContext db,
                    CurrentUser currentUser,
                    CancellationToken ct
                ) =>
                {
                    var me = await currentUser.GetIdAsync(ct);

                    // Owner filter inside the same query, so another user's story is a 404 rather
                    // than a 403: a 403 confirms the id exists, which the caller has no use for.
                    var story = await db
                        .Stories.Where(s => s.Id == id && s.UserId == me)
                        .Select(ToStoryDto)
                        .FirstOrDefaultAsync(ct);

                    return story is null ? Results.NotFound() : Results.Ok(story);
                }
            )
            .WithName("GetStory");

        stories
            .MapPatch(
                "/{id:guid}",
                async (
                    Guid id,
                    UpdateStoryTitleRequest request,
                    StoryWalkerDbContext db,
                    CurrentUser currentUser,
                    CancellationToken ct
                ) =>
                {
                    if (string.IsNullOrWhiteSpace(request.Title))
                    {
                        return Results.ValidationProblem(
                            new Dictionary<string, string[]> { ["title"] = ["Title is required."] }
                        );
                    }

                    var me = await currentUser.GetIdAsync(ct);
                    var story = await db.Stories.FirstOrDefaultAsync(
                        s => s.Id == id && s.UserId == me,
                        ct
                    );
                    if (story is null)
                    {
                        return Results.NotFound();
                    }

                    // A failed story can only be deleted (ADR-0026); the dashboard hides the
                    // rename action, and this keeps any other client to the same rule.
                    if (story.Status == StoryStatus.Failed)
                    {
                        return Results.Problem(
                            detail: "A story that failed can only be deleted.",
                            statusCode: StatusCodes.Status409Conflict
                        );
                    }

                    story.Title = request.Title.Trim();
                    await db.SaveChangesAsync(ct);

                    // Re-project the saved story so the client gets the updated representation.
                    var updated = await db
                        .Stories.Where(s => s.Id == id && s.UserId == me)
                        .Select(ToStoryDto)
                        .FirstAsync(ct);

                    return Results.Ok(updated);
                }
            )
            .WithName("UpdateStoryTitle");

        stories
            .MapDelete(
                "/{id:guid}",
                async (
                    Guid id,
                    StoryWalkerDbContext db,
                    CurrentUser currentUser,
                    CancellationToken ct
                ) =>
                {
                    var me = await currentUser.GetIdAsync(ct);
                    var story = await db.Stories.FirstOrDefaultAsync(
                        s => s.Id == id && s.UserId == me,
                        ct
                    );
                    if (story is null)
                    {
                        return Results.NotFound();
                    }

                    db.Stories.Remove(story);

                    // Pages cascade (StoryConfiguration). Nothing else references a story: every
                    // story is self-contained (ADR-0019), so the delete cannot conflict.
                    await db.SaveChangesAsync(ct);

                    return Results.NoContent();
                }
            )
            .WithName("DeleteStory");

        return app;
    }

    /// <summary>
    /// Persists a <see cref="Story"/> in <see cref="StoryStatus.Generating"/> from the authored brief
    /// and enqueues that brief for the generation worker (ADR-0003). Only the story row is created
    /// here; the worker's <c>PersistPages</c> activity writes the pages.
    /// </summary>
    private static async Task<Story> StartGenerationAsync(
        StoryWalkerDbContext db,
        IBriefPublisher publisher,
        Guid userId,
        Guid conversationId,
        GenerationBrief brief,
        CancellationToken ct
    )
    {
        var story = new Story
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            // Where a failure is reported back to, with the agent's advice (ADR-0027).
            ConversationId = conversationId,
            Title = brief.Title,
            TargetAge = brief.TargetAge,
            Status = StoryStatus.Generating,
            ArtStyle = brief.ArtStyle,
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
