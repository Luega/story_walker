using StoryWalker.Application.Brief;

namespace StoryWalker.Application.Agent;

/// <summary>
/// The conversational agent that interviews the user toward a generation brief (ADR-0011).
/// One implementation drives the chat backend (a Foundry chat-model deployment) through
/// <c>Microsoft.Extensions.AI</c>, so the same logic runs locally and in the cloud.
/// </summary>
public interface IStoryAgent
{
    /// <summary>
    /// Sends one user turn and returns the agent's reply. Pass <paramref name="conversationId"/>
    /// as <c>null</c> to start a new conversation (the returned id continues it on later turns).
    /// </summary>
    Task<AgentTurnResult> SendAsync(
        string? conversationId,
        string message,
        CancellationToken ct = default
    );
}

/// <param name="ConversationId">Server-generated id the client round-trips to continue the conversation.</param>
/// <param name="Reply">The agent's reply text for this turn.</param>
/// <param name="BriefReady">True once the agent has emitted a complete generation brief (ADR-0013).</param>
/// <param name="Brief">The authored, validated brief on the final turn; null on every earlier turn.</param>
public record AgentTurnResult(
    string ConversationId,
    string Reply,
    bool BriefReady,
    GenerationBrief? Brief = null
);
