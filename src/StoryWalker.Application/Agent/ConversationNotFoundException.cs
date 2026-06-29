namespace StoryWalker.Application.Agent;

/// <summary>
/// Thrown when a turn references a conversation id the store doesn't know (unknown or malformed).
/// The endpoint maps this to 404 so the client restarts cleanly, rather than silently losing
/// context the way an in-memory store would after a restart.
/// </summary>
public sealed class ConversationNotFoundException(string conversationId)
    : Exception($"Conversation '{conversationId}' was not found.")
{
    public string ConversationId { get; } = conversationId;
}
