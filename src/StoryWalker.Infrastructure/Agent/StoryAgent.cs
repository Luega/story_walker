using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using StoryWalker.Application.Agent;
using StoryWalker.Application.Brief;
using StoryWalker.Domain.Entities;
using StoryWalker.Domain.Enums;
using StoryWalker.Infrastructure.Persistence;

namespace StoryWalker.Infrastructure.Agent;

/// <summary>
/// The single agent implementation, written against <see cref="IChatClient"/> rather than a
/// concrete SDK; the registered client is a Foundry chat-model deployment (ADR-0011).
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
            if (conversation.Status == ConversationStatus.Completed)
            {
                return new AgentTurnResult(
                    conversation.Id.ToString(),
                    "The conversation has already completed. Wait for the story generation to finish, or start a new conversation.",
                    BriefReady: true
                );
            }
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

        var briefReady = response.Messages.Any(m => m.Text.Contains("__END__CHAT__"));
        conversation.Status = briefReady ? ConversationStatus.Completed : ConversationStatus.Active;

        // Author the brief in the same turn the user confirms, so BriefReady and the brief arrive
        // together. A validation failure throws before SaveChanges, rolling back the turn so the
        // client can resend the confirmation and author again (ADR-0013).
        var brief = briefReady ? await AuthorBriefAsync(conversation, ct) : null;

        await db.SaveChangesAsync(ct);

        return new AgentTurnResult(
            conversation.Id.ToString(),
            response.Text,
            BriefReady: briefReady,
            Brief: brief
        );
    }

    /// <summary>
    /// Second, structured-output pass (ADR-0013): re-reads the confirmed conversation and authors
    /// the complete <see cref="GenerationBrief"/> as schema-shaped JSON. Character reuse is deferred
    /// until pgvector lands (ADR-0009), so every character is authored as new
    /// (<c>existingCharacterId</c>/<c>basedOnStoryId</c> forced null). Retries once on invalid
    /// output before giving up.
    /// </summary>
    private async Task<GenerationBrief> AuthorBriefAsync(
        Conversation conversation,
        CancellationToken ct
    )
    {
        // Drop the interview system prompt and lead with the authoring one; the confirmed
        // interview transcript becomes the context the author works from.
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, StoryAgentPrompt.BriefAuthoringInstructions),
        };
        messages.AddRange(
            conversation
                .Messages.Where(m => m.Role != MessageRole.System)
                .OrderBy(m => m.Seq)
                .Select(m => new ChatMessage(ToChatRole(m.Role), m.Content))
        );
        messages.Add(
            new ChatMessage(ChatRole.User, "Output the generation brief for the confirmed plan.")
        );

        IReadOnlyList<string> errors = [];
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var response = await chatClient.GetResponseAsync<GenerationBrief>(
                messages,
                cancellationToken: ct
            );

            if (response.TryGetResult(out var brief))
            {
                errors = GenerationBriefValidator.Validate(brief);
                if (errors.Count == 0)
                {
                    return brief;
                }
            }
            else
            {
                errors = ["The model did not return a parseable generation brief."];
            }
        }

        throw new BriefValidationException(errors);
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
