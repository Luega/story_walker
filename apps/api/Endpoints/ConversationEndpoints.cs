using Microsoft.EntityFrameworkCore;
using StoryWalker.Api.Auth;
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

                    var rows = await db
                        .Conversations.Where(c => c.UserId == me)
                        .Select(c => new
                        {
                            c.Id,
                            c.Title,
                            c.Status,
                            LatestStory = db
                                .Stories.Where(s => s.ConversationId == c.Id)
                                .OrderByDescending(s => s.CreatedAt)
                                .Select(s => new { s.Title, s.Status })
                                .FirstOrDefault(),
                            FirstUserMessage = c
                                .Messages.Where(m => m.Role == MessageRole.User)
                                .OrderBy(m => m.Seq)
                                .Select(m => m.Content)
                                .FirstOrDefault(),
                            // Adding a message never touches the conversation row, so its own
                            // timestamps say nothing about activity.
                            LastActivityAt = c.Messages.Max(m => (DateTime?)m.CreatedAt) ?? c.CreatedAt,
                        })
                        .OrderByDescending(c => c.LastActivityAt)
                        .ToListAsync(ct);

                    return Results.Ok(
                        rows.Select(c => new ConversationSummaryDto(
                            c.Id,
                            DisplayTitle(c.Title, c.LatestStory?.Title, c.FirstUserMessage),
                            c.Status.ToString(),
                            Resumable: c.Status == ConversationStatus.Active,
                            StoryFailed: c.LatestStory?.Status == StoryStatus.Failed,
                            c.LastActivityAt
                        ))
                    );
                }
            )
            .WithName("ListConversations");

        return app;
    }

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
