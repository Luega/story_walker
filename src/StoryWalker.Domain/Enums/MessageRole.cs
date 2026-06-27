namespace StoryWalker.Domain.Enums;

/// <summary>
/// Author of a conversation message. A domain-owned enum (kept free of any chat-SDK type, per
/// ADR-0015); the agent adapter maps it to/from the chat client's role on the way in and out.
/// </summary>
public enum MessageRole
{
    System,
    User,
    Assistant,
}
