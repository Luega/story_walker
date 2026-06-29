using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using StoryWalker.Application.Agent;
using StoryWalker.Domain.Entities;
using StoryWalker.Domain.Enums;
using StoryWalker.Infrastructure.Persistence;

namespace StoryWalker.Infrastructure.Agent;

/// <summary>
/// The single agent implementation, provider-agnostic over <see cref="IChatClient"/>: the same
/// logic runs against local Ollama and a Foundry chat-model deployment (ADR-0011 refinement).
/// Conversation history is persisted (ADR-0018) so it survives restarts and multiple instances;
/// each turn loads the stored messages, sends the full history to the model, and appends the new
/// turns. A new conversation is seeded with the <see cref="StoryAgentPrompt"/> system message.
/// </summary>
public sealed class StoryAgent(StoryWalkerDbContext db, IChatClient chatClient) : IStoryAgent
{
    public async Task<AgentTurnResult> SendAsync(
        string? conversationId,
        string message,
        CancellationToken ct = default
    )
    {
        Conversation conversation;
        if (string.IsNullOrEmpty(conversationId))
        {
            conversation = new Conversation
            {
                Id = Guid.NewGuid(),
                Status = ConversationStatus.Active,
            };
            db.Conversations.Add(conversation);
            Append(conversation, MessageRole.System, StoryAgentPrompt.Instructions);
        }
        else
        {
            conversation = await LoadConversationAsync(conversationId, ct);
        }

        Append(conversation, MessageRole.User, message);

        // Send the whole conversation so far; chat completions are stateless.
        var history = conversation
            .Messages.OrderBy(m => m.Seq)
            .Select(m => new ChatMessage(ToChatRole(m.Role), m.Content))
            .ToList();

        var response = await chatClient.GetResponseAsync(history, cancellationToken: ct);

        foreach (var reply in response.Messages)
        {
            Append(conversation, ToMessageRole(reply.Role), reply.Text);
        }

        await db.SaveChangesAsync(ct);

        var briefReady = response.Messages.Any(m => m.Text.Contains("__END__CHAT__"));
        return new AgentTurnResult(
            conversation.Id.ToString(),
            response.Text,
            BriefReady: briefReady
        );
    }

    /// <summary>
    /// Appends a message to the conversation graph and tracks it as inserted. Adding to the DbSet
    /// explicitly (rather than relying on the navigation) keeps EF from mis-detecting a new message
    /// with a client-set id on an already-tracked conversation as a modified row.
    /// </summary>
    private void Append(Conversation conversation, MessageRole role, string content)
    {
        var message = new ConversationMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            Seq = conversation.Messages.Count,
            Role = role,
            Content = content,
        };

        conversation.Messages.Add(message);
        db.ConversationMessages.Add(message);
    }

    private async Task<Conversation> LoadConversationAsync(
        string conversationId,
        CancellationToken ct
    )
    {
        // An unknown or malformed id is a not-found, so the client restarts cleanly instead of
        // silently continuing an empty conversation.
        if (!Guid.TryParse(conversationId, out var id))
        {
            throw new ConversationNotFoundException(conversationId);
        }

        return await db
                .Conversations.Include(c => c.Messages)
                .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new ConversationNotFoundException(conversationId);
    }

    private static ChatRole ToChatRole(MessageRole role) =>
        role switch
        {
            MessageRole.System => ChatRole.System,
            MessageRole.Assistant => ChatRole.Assistant,
            _ => ChatRole.User,
        };

    private static MessageRole ToMessageRole(ChatRole role) =>
        role.Value switch
        {
            "system" => MessageRole.System,
            "assistant" => MessageRole.Assistant,
            _ => MessageRole.User,
        };
}
