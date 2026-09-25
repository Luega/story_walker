using Microsoft.Extensions.AI;
using StoryWalker.Domain.Enums;

namespace StoryWalker.Infrastructure.Agent;

/// <summary>
/// Maps the domain's <see cref="MessageRole"/> to and from the chat client's role (ADR-0015). Shared
/// by everything that replays a stored conversation to the model: the agent itself, and the worker
/// when it reports a failed story back to its conversation (ADR-0027).
/// </summary>
internal static class ChatRoles
{
    public static ChatRole ToChatRole(MessageRole role) =>
        role switch
        {
            MessageRole.System => ChatRole.System,
            MessageRole.Assistant => ChatRole.Assistant,
            _ => ChatRole.User,
        };

    public static MessageRole ToMessageRole(ChatRole role) =>
        role.Value switch
        {
            "system" => MessageRole.System,
            "assistant" => MessageRole.Assistant,
            _ => MessageRole.User,
        };
}
