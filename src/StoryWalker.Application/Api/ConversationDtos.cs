namespace StoryWalker.Application.Api;

// REST DTOs for the conversation history (ADR-0028). Status is a string, as in StoryDtos, to keep
// the wire contract free of a Domain dependency.

/// <summary>One row of the conversation history sidebar.</summary>
/// <param name="Title">
/// The user's own title, or else the latest linked story's, or else the start of the first
/// message: computed on read, never stored as a default.
/// </param>
/// <param name="Resumable">True while the conversation can still be continued (<c>Active</c>).</param>
/// <param name="StoryFailed">True when the latest story queued from it failed (ADR-0027).</param>
/// <param name="LastActivityAt">When its latest message was written.</param>
public record ConversationSummaryDto(
    Guid Id,
    string Title,
    string Status,
    bool Resumable,
    bool StoryFailed,
    DateTime LastActivityAt
);

/// <summary>
/// A conversation as the chat modal shows it: only what the parent and the agent said. System
/// messages (instructions, saved context, failure notes) are left out, and the end-of-chat
/// sentinel is stripped from the agent's messages (ADR-0027, ADR-0028).
/// </summary>
public record ConversationDto(
    Guid Id,
    string Title,
    string Status,
    bool Resumable,
    IReadOnlyList<ConversationMessageDto> Messages
);

/// <param name="Role"><c>user</c> or <c>assistant</c>.</param>
public record ConversationMessageDto(string Role, string Text);
