using StoryWalker.Domain.Enums;

namespace StoryWalker.Domain.Entities;

/// <summary>
/// An agent conversation that interviews the user toward a generation brief (ADR-0011). Persisted
/// so it survives restarts and works across API instances, and so unfinished ones can be found and
/// cleaned up. Its <see cref="EntityBase.Id"/> is the conversation id handed to the client.
/// </summary>
public class Conversation : EntityBase
{
    /// <summary>The owning user (ADR-0002). Every conversation has one from its first turn.</summary>
    public Guid UserId { get; set; }

    public ConversationStatus Status { get; set; }

    /// <summary>
    /// A title the user chose in the conversation history, or null. Only the user's choice is
    /// stored: the title shown defaults to the latest linked story's, computed on read, so it
    /// follows new stories without any syncing (ADR-0028).
    /// </summary>
    public string? Title { get; set; }

    /// <summary>The full message history, in <see cref="ConversationMessage.Seq"/> order.</summary>
    public ICollection<ConversationMessage> Messages { get; set; } = [];
}
