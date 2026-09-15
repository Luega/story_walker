using System.Collections.Concurrent;
using StoryWalker.Application.Agent;
using StoryWalker.Application.Brief;

namespace StoryWalker.Api.Tests;

/// <summary>
/// Test double for <see cref="IStoryAgent"/> so the endpoint contract can be exercised without a
/// chat backend. It mints an id on the first turn and echoes the same id thereafter, and mirrors
/// the real agent's not-found behaviour: an id it never issued throws
/// <see cref="ConversationNotFoundException"/> (so the endpoint's 404 path is covered). Sending
/// <see cref="ConfirmMessage"/> makes it emit a ready brief, driving the endpoint's enqueue path.
/// </summary>
internal sealed class FakeStoryAgent : IStoryAgent
{
    /// <summary>Sentinel message that makes the fake agent return a ready brief.</summary>
    public const string ConfirmMessage = "confirm-brief";

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

    private readonly ConcurrentDictionary<string, byte> _issued = new();

    public Task<AgentTurnResult> SendAsync(
        string? conversationId,
        string message,
        CancellationToken ct = default
    )
    {
        if (!string.IsNullOrEmpty(conversationId) && !_issued.ContainsKey(conversationId))
        {
            throw new ConversationNotFoundException(conversationId);
        }

        var id = conversationId ?? Guid.NewGuid().ToString();
        _issued[id] = 0;

        return Task.FromResult(
            message == ConfirmMessage
                ? new AgentTurnResult(id, "Brief ready.", BriefReady: true, SampleBrief)
                : new AgentTurnResult(id, $"Echo: {message}", BriefReady: false)
        );
    }
}
