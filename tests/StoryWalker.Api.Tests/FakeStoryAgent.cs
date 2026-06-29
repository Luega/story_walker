using System.Collections.Concurrent;
using StoryWalker.Application.Agent;

namespace StoryWalker.Api.Tests;

/// <summary>
/// Test double for <see cref="IStoryAgent"/> so the endpoint contract can be exercised without a
/// chat backend. It mints an id on the first turn and echoes the same id thereafter, and mirrors
/// the real agent's not-found behaviour: an id it never issued throws
/// <see cref="ConversationNotFoundException"/> (so the endpoint's 404 path is covered).
/// </summary>
internal sealed class FakeStoryAgent : IStoryAgent
{
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

        return Task.FromResult(new AgentTurnResult(id, $"Echo: {message}", BriefReady: false));
    }
}
