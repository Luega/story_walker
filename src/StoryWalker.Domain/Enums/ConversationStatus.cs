namespace StoryWalker.Domain.Enums;

/// <summary>
/// Lifecycle of an agent conversation. <see cref="Active"/> until the agent emits a complete
/// brief (ADR-0013), then <see cref="Completed"/>. Stored as a string for readability and so a
/// retention job can find stale <see cref="Active"/> conversations to purge.
/// </summary>
public enum ConversationStatus
{
    Active,
    Completed,
}
