using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using StoryWalker.Application.Agent;
using StoryWalker.Application.Brief;
using StoryWalker.Domain.Entities;
using StoryWalker.Domain.Enums;
using StoryWalker.Infrastructure.Persistence;

namespace StoryWalker.Api.Tests;

/// <summary>
/// Test double for <see cref="IStoryAgent"/> so the endpoint contract can be exercised without a
/// chat backend. It mints an id on the first turn and echoes the same id thereafter, and mirrors
/// the real agent's not-found behaviour: an id it never issued, or issued to another user, throws
/// <see cref="ConversationNotFoundException"/> (so the endpoint's 404 path is covered). Sending
/// <see cref="ConfirmMessage"/> makes it emit a ready brief, driving the endpoint's enqueue path;
/// sending <see cref="FilteredMessage"/> makes it throw <see cref="ContentFilteredException"/>, as the
/// real agent does when the model's content filter refuses a turn.
/// <para>
/// Like the real agent, it writes a <see cref="Conversation"/> row for every id it issues, so a
/// story queued from it can point at its conversation (ADR-0027) without breaking the foreign key.
/// </para>
/// </summary>
internal sealed class FakeStoryAgent(IServiceScopeFactory scopes) : IStoryAgent
{
    /// <summary>Sentinel message that makes the fake agent return a ready brief.</summary>
    public const string ConfirmMessage = "confirm-brief";

    /// <summary>Sentinel message that makes the fake agent report a content-filter refusal.</summary>
    public const string FilteredMessage = "filtered-by-content-policy";

    /// <summary>
    /// The brief the fake agent emits on <see cref="ConfirmMessage"/>. Six pages, because
    /// <see cref="GenerationBriefValidator.PageCount"/> is an exact count, not a cap.
    /// </summary>
    public static readonly GenerationBrief SampleBrief = new(
        Title: "The Fox and the Moon",
        TargetAge: 6,
        ArtStyle: "soft watercolour, midnight blues",
        Pages:
        [
            .. Enumerable
                .Range(1, GenerationBriefValidator.PageCount)
                .Select(n => new BriefPage(
                    n,
                    $"The fox looked up at the moon. (page {n})",
                    "Fox, a small red fox with a white-tipped tail, gazing at the moon"
                )),
        ]
    );

    private readonly ConcurrentDictionary<string, Guid> _issued = new();

    public async Task<AgentTurnResult> SendAsync(
        Guid userId,
        string? conversationId,
        string message,
        Guid? basedOnStoryId = null,
        CancellationToken ct = default
    )
    {
        // Unknown id, or an id issued to another user: both are not-found, as in the real agent.
        if (
            !string.IsNullOrEmpty(conversationId)
            && (!_issued.TryGetValue(conversationId, out var owner) || owner != userId)
        )
        {
            throw new ConversationNotFoundException(conversationId);
        }

        if (message == FilteredMessage)
        {
            throw new ContentFilteredException();
        }

        var id = conversationId;
        if (id is null)
        {
            id = Guid.NewGuid().ToString();
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<StoryWalkerDbContext>();
            db.Conversations.Add(
                new Conversation { Id = Guid.Parse(id), UserId = userId, Status = ConversationStatus.Active }
            );
            await db.SaveChangesAsync(ct);
        }
        _issued[id] = userId;

        return message == ConfirmMessage
            ? new AgentTurnResult(id, "Brief ready.", BriefReady: true, SampleBrief)
            : new AgentTurnResult(id, $"Echo: {message}", BriefReady: false);
    }
}
