using System.ClientModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using StoryWalker.Application.Agent;
using StoryWalker.Application.Api;
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
/// turns. A new conversation is seeded with the <see cref="StoryAgentPrompt"/> system message,
/// composed with whatever standing context the user has saved on their settings page.
/// </summary>
public sealed class StoryAgent(StoryWalkerDbContext db, IChatClient chatClient) : IStoryAgent
{
    public async Task<AgentTurnResult> SendAsync(
        Guid userId,
        string? conversationId,
        string message,
        Guid? basedOnStoryId = null,
        CancellationToken ct = default
    )
    {
        Conversation conversation;
        if (string.IsNullOrEmpty(conversationId))
        {
            // Checked before anything is added, so a bad starting point saves nothing.
            var previousStory = basedOnStoryId is { } storyId
                ? await PreviousStoryAsync(userId, storyId, ct)
                    ?? throw new BasedOnStoryNotFoundException(storyId)
                : null;

            conversation = new Conversation
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Status = ConversationStatus.Active,
                BasedOnStoryId = basedOnStoryId,
            };
            db.Conversations.Add(conversation);

            // The user's standing context, read here and only here. Composing it into the system
            // message means the conversation keeps the context it started with: editing the
            // settings page never rewrites a chat already under way, because this message is
            // persisted (ADR-0018) and never recomputed. One extra query, on the first turn only.
            var settings = await db
                .Users.Where(u => u.Id == userId)
                .Select(u => new UserSettingsDto(
                    u.KidsContext,
                    u.StoryContext,
                    u.CharactersContext,
                    u.ArtStyleContext
                ))
                .FirstOrDefaultAsync(ct);

            // A related story (ADR-0029) also starts from the one it builds on.
            var system = StoryAgentPrompt.Compose(settings);
            if (previousStory is not null)
            {
                system += "\n\n" + previousStory;
            }
            Append(conversation, MessageRole.System, system);
        }
        else
        {
            conversation = await LoadConversationAsync(userId, conversationId, ct);
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
            .Select(m => new ChatMessage(ChatRoles.ToChatRole(m.Role), m.Content))
            .ToList();

        var response = await RefusalAwareAsync(
            () => chatClient.GetResponseAsync(history, cancellationToken: ct)
        );

        foreach (var reply in response.Messages)
        {
            Append(conversation, ChatRoles.ToMessageRole(reply.Role), reply.Text);
        }

        var briefReady = response.Messages.Any(m =>
            m.Text.Contains(StoryAgentPrompt.EndOfChatSentinel, StringComparison.Ordinal)
        );
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

        // The system messages below are dropped, and with them the previous story a related one
        // builds on; without it the author would not know the characters it has to keep
        // (ADR-0029). Rebuilt from the story, which is gone only if it was deleted meanwhile.
        if (
            conversation.BasedOnStoryId is { } basedOn
            && await PreviousStoryAsync(conversation.UserId, basedOn, ct) is { } previousStory
        )
        {
            messages.Add(new ChatMessage(ChatRole.System, previousStory));
        }
        messages.AddRange(
            conversation
                .Messages.Where(m => m.Role != MessageRole.System)
                .OrderBy(m => m.Seq)
                .Select(m => new ChatMessage(ChatRoles.ToChatRole(m.Role), m.Content))
        );
        messages.Add(
            new ChatMessage(ChatRole.User, "Output the generation brief for the confirmed plan.")
        );

        IReadOnlyList<string> errors = [];
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var response = await RefusalAwareAsync(
                () => chatClient.GetResponseAsync<GenerationBrief>(messages, cancellationToken: ct)
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
    /// Runs one model call and turns a content-filter refusal into
    /// <see cref="ContentFilteredException"/> (ADR-0024). Azure refuses in two ways: a filtered
    /// prompt is a 400 whose error code is <c>content_filter</c>, and a filtered reply comes back
    /// with that finish reason. Both surface before <c>SaveChanges</c>, so the turn rolls back.
    /// </summary>
    private static async Task<TResponse> RefusalAwareAsync<TResponse>(Func<Task<TResponse>> call)
        where TResponse : ChatResponse
    {
        TResponse response;
        try
        {
            response = await call();
        }
        catch (ClientResultException ex)
            when (ex.Message.Contains("content_filter", StringComparison.Ordinal))
        {
            throw new ContentFilteredException(ex);
        }

        return response.FinishReason == ChatFinishReason.ContentFilter
            ? throw new ContentFilteredException()
            : response;
    }

    /// <summary>
    /// The previous-story block for one of the user's <c>Complete</c> stories, or null when there is
    /// no such story: unknown, someone else's, or not finished (ADR-0029).
    /// </summary>
    private async Task<string?> PreviousStoryAsync(Guid userId, Guid storyId, CancellationToken ct)
    {
        var story = await db
            .Stories.Where(s =>
                s.Id == storyId && s.UserId == userId && s.Status == StoryStatus.Complete
            )
            .Select(s => new
            {
                s.Title,
                s.TargetAge,
                s.ArtStyle,
                Pages = s
                    .Pages.Select(p => new PreviousStoryPage(p.PageNo, p.Content, p.IllustrationPrompt))
                    .ToList(),
            })
            .FirstOrDefaultAsync(ct);

        return story is null
            ? null
            : StoryAgentPrompt.PreviousStory(story.Title, story.TargetAge, story.ArtStyle, story.Pages);
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
        Guid userId,
        string conversationId,
        CancellationToken ct
    )
    {
        // An unknown or malformed id is a not-found, so the client restarts cleanly instead of
        // silently continuing an empty conversation. Someone else's id takes the same path: the
        // owner filter lives in the lookup, so no caller can forget it.
        if (!Guid.TryParse(conversationId, out var id))
        {
            throw new ConversationNotFoundException(conversationId);
        }

        return await db
                .Conversations.Include(c => c.Messages)
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct)
            ?? throw new ConversationNotFoundException(conversationId);
    }
}
