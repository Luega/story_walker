using Microsoft.EntityFrameworkCore;
using StoryWalker.Api.Auth;
using StoryWalker.Application.Agent;
using StoryWalker.Application.Api;
using StoryWalker.Domain.Enums;
using StoryWalker.Infrastructure.Persistence;

namespace StoryWalker.Api.Endpoints;

/// <summary>
/// The conversation history (ADR-0028): list, read, retitle and delete the signed-in user's
/// conversations with the agent. Continuing one is the existing <c>POST /stories/conversations</c>
/// with its id; this group only exposes what the history sidebar needs. Every lookup filters on the
/// owner, so another user's conversation is a 404, never a 403.
/// </summary>
public static class ConversationEndpoints
{
    /// <summary>How much of the first message stands in for a title when nothing better exists.</summary>
    private const int PreviewLength = 60;

    public static IEndpointRouteBuilder MapConversationEndpoints(this IEndpointRouteBuilder app)
    {
        var conversations = app
            .MapGroup("/stories/conversations")
            .WithTags("Conversations")
            .RequireAuthorization();

        conversations
            .MapGet(
                "/",
                async (StoryWalkerDbContext db, CurrentUser currentUser, CancellationToken ct) =>
                {
                    var me = await currentUser.GetIdAsync(ct);

                    var rows = await Summaries(db, me)
                        .OrderByDescending(c => c.LastActivityAt)
                        .ToListAsync(ct);

                    return Results.Ok(rows.Select(ToSummaryDto));
                }
            )
            .WithName("ListConversations");

        conversations
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

                    var summary = await Summaries(db, me).FirstOrDefaultAsync(c => c.Id == id, ct);
                    if (summary is null)
                    {
                        return Results.NotFound();
                    }

                    // System messages are instructions, saved context and failure notes: never
                    // for the parent's eyes (ADR-0027).
                    var messages = await db
                        .ConversationMessages.Where(m =>
                            m.ConversationId == id && m.Role != MessageRole.System
                        )
                        .OrderBy(m => m.Seq)
                        .Select(m => new { m.Role, m.Content })
                        .ToListAsync(ct);

                    return Results.Ok(
                        new ConversationDto(
                            summary.Id,
                            ToSummaryDto(summary).Title,
                            summary.Status.ToString(),
                            summary.Status == ConversationStatus.Active,
                            messages
                                .Select(m => new ConversationMessageDto(
                                    m.Role == MessageRole.User ? "user" : "assistant",
                                    m.Content.Replace(
                                            StoryAgentPrompt.EndOfChatSentinel,
                                            string.Empty,
                                            StringComparison.Ordinal
                                        )
                                        .Trim()
                                ))
                                .ToList()
                        )
                    );
                }
            )
            .WithName("GetConversation");

        conversations
            .MapPatch(
                "/{id:guid}",
                async (
                    Guid id,
                    UpdateConversationTitleRequest request,
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
                    var conversation = await db.Conversations.FirstOrDefaultAsync(
                        c => c.Id == id && c.UserId == me,
                        ct
                    );
                    if (conversation is null)
                    {
                        return Results.NotFound();
                    }

                    conversation.Title = request.Title.Trim();
                    await db.SaveChangesAsync(ct);

                    var summary = await Summaries(db, me).FirstAsync(c => c.Id == id, ct);
                    return Results.Ok(ToSummaryDto(summary));
                }
            )
            .WithName("UpdateConversationTitle");

        return app;
    }

    /// <summary>
    /// What a history row is computed from, per conversation, in one query. Init properties rather
    /// than a positional record: EF can go on filtering and sorting after a member-init projection,
    /// but not after a constructor call.
    /// </summary>
    private sealed class SummaryRow
    {
        public Guid Id { get; init; }
        public string? Title { get; init; }
        public ConversationStatus Status { get; init; }
        public string? LatestStoryTitle { get; init; }
        public StoryStatus? LatestStoryStatus { get; init; }
        public string? FirstUserMessage { get; init; }
        public DateTime LastActivityAt { get; init; }
    }

    /// <summary>The caller's conversations, with what their title and flags are computed from.</summary>
    private static IQueryable<SummaryRow> Summaries(StoryWalkerDbContext db, Guid me) =>
        db.Conversations.Where(c => c.UserId == me)
            .Select(c => new SummaryRow
            {
                Id = c.Id,
                Title = c.Title,
                Status = c.Status,
                LatestStoryTitle = db
                    .Stories.Where(s => s.ConversationId == c.Id)
                    .OrderByDescending(s => s.CreatedAt)
                    .Select(s => s.Title)
                    .FirstOrDefault(),
                LatestStoryStatus = db
                    .Stories.Where(s => s.ConversationId == c.Id)
                    .OrderByDescending(s => s.CreatedAt)
                    .Select(s => (StoryStatus?)s.Status)
                    .FirstOrDefault(),
                FirstUserMessage = c
                    .Messages.Where(m => m.Role == MessageRole.User)
                    .OrderBy(m => m.Seq)
                    .Select(m => m.Content)
                    .FirstOrDefault(),
                // Adding a message never touches the conversation row, so its own timestamps say
                // nothing about activity.
                LastActivityAt = c.Messages.Max(m => (DateTime?)m.CreatedAt) ?? c.CreatedAt,
            });

    private static ConversationSummaryDto ToSummaryDto(SummaryRow c) =>
        new(
            c.Id,
            DisplayTitle(c.Title, c.LatestStoryTitle, c.FirstUserMessage),
            c.Status.ToString(),
            Resumable: c.Status == ConversationStatus.Active,
            StoryFailed: c.LatestStoryStatus == StoryStatus.Failed,
            c.LastActivityAt
        );

    /// <summary>
    /// The user's own title wins; else the latest story queued from the conversation, so the default
    /// follows new stories with nothing to sync (ADR-0028); else the start of the first message.
    /// </summary>
    private static string DisplayTitle(string? own, string? latestStory, string? firstMessage)
    {
        if (!string.IsNullOrWhiteSpace(own))
        {
            return own;
        }

        if (!string.IsNullOrWhiteSpace(latestStory))
        {
            return latestStory;
        }

        var preview = (firstMessage ?? string.Empty).ReplaceLineEndings(" ").Trim();
        if (preview.Length == 0)
        {
            return "New conversation";
        }

        return preview.Length <= PreviewLength ? preview : preview[..PreviewLength].TrimEnd() + "…";
    }
}
