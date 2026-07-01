using StoryWalker.Application.Brief;

namespace StoryWalker.Application.Api;

// Wire shape for the agent-chat endpoint (POST /stories/conversations). The conversation id is
// provider-agnostic: the server issues it on the first turn and the client round-trips it.

/// <param name="ConversationId">Continue an existing conversation; null to start a new one.</param>
public record AgentMessageRequest(
    string? ConversationId,
    string Message);

/// <param name="ConversationId">The conversation to continue on the next turn.</param>
/// <param name="BriefReady">True when the agent has emitted a complete brief and generation can start.</param>
/// <param name="Brief">The authored, validated generation brief on the final turn; null otherwise.</param>
public record AgentMessageResponse(
    string ConversationId,
    string Reply,
    bool BriefReady,
    GenerationBrief? Brief = null);
