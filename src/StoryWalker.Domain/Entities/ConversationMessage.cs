using StoryWalker.Domain.Enums;

namespace StoryWalker.Domain.Entities;

/// <summary>
/// One message in a <see cref="Conversation"/>. Append-only. <see cref="Seq"/> gives a stable
/// order within a conversation — <see cref="EntityBase.CreatedAt"/> can't, since every message
/// saved in the same turn shares one timestamp.
/// </summary>
public class ConversationMessage : EntityBase
{
    public Guid ConversationId { get; set; }

    /// <summary>Zero-based position in the conversation; unique per conversation.</summary>
    public int Seq { get; set; }

    public MessageRole Role { get; set; }

    public required string Content { get; set; }

    public Conversation Conversation { get; set; } = null!;
}
