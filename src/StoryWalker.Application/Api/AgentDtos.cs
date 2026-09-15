namespace StoryWalker.Application.Api;

// Wire shape for the agent-chat endpoint (POST /stories/conversations). The conversation id is
// provider-agnostic: the server issues it on the first turn and the client round-trips it.

/// <param name="ConversationId">Continue an existing conversation; null to start a new one.</param>
public record AgentMessageRequest(
    string? ConversationId,
    string Message);

/// <param name="ConversationId">The conversation to continue on the next turn.</param>
/// <param name="BriefReady">True when the agent has emitted a complete brief and generation can start.</param>
public record AgentMessageResponse(
    string ConversationId,
    string Reply,
    bool BriefReady);

/// <summary>
/// Returned with 202 Accepted when the agent has authored a brief: the story has been persisted
/// (<c>status = Generating</c>) and its brief enqueued for the generation worker (ADR-0003). Clients
/// poll <c>GET /stories/{storyId}</c> for progress. The brief itself is not returned — it goes on the
/// queue, not to the browser.
/// </summary>
public record StoryQueuedResponse(
    Guid StoryId,
    string Status);
