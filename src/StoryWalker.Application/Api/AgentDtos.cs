namespace StoryWalker.Application.Api;

// Minimal agent-chat shape — tentative. The Foundry information-gathering agent (ADR-0011)
// endpoint is not yet designed; refine these when it is.

/// <param name="StoryId">Set to continue an existing draft conversation; null to start a new one.</param>
public record AgentMessageRequest(
    Guid? StoryId,
    string Message);

/// <param name="BriefReady">True when the agent has emitted a complete brief and generation can start.</param>
public record AgentMessageResponse(
    string Reply,
    bool BriefReady);
