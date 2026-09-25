using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using StoryWalker.Application.Agent;
using StoryWalker.Domain.Entities;
using StoryWalker.Domain.Enums;
using StoryWalker.Infrastructure.Agent;
using StoryWalker.Infrastructure.Persistence;

namespace StoryWalker.Infrastructure.Generation;

/// <summary>
/// Reports a failed story back to the conversation that queued it (ADR-0027). It appends two
/// messages: a system note the parent never sees, which tells the agent what went wrong in terms
/// of a category and a page, and the agent's answer to it, which is what the parent reads when
/// they come back. Then it reopens the conversation, so it can be carried on from the
/// confirmation that failed.
/// <para>
/// Recording happens only while the conversation is still <see cref="ConversationStatus.Completed"/>,
/// which is what it was when generation started; reopening it is what marks the report as done.
/// So a redelivered message cannot append the report twice.
/// </para>
/// </summary>
public sealed class GenerationFailureRecorder(
    StoryWalkerDbContext db,
    IChatClient chatClient,
    ILogger<GenerationFailureRecorder> logger
)
{
    /// <summary>
    /// What the parent reads when the agent could not be asked: a failed chat call, a reply the
    /// chat model's filter stopped, or nothing left once the sentinel is stripped.
    /// </summary>
    public const string FallbackAdvice =
        "I'm sorry, I couldn't finish illustrating your story. Would you like to change something in the plan and try again?";

    public async Task RecordAsync(
        Guid storyId,
        int pageNo,
        GenerationFailureReason reason,
        CancellationToken ct = default
    )
    {
        var conversationId = await db
            .Stories.Where(s => s.Id == storyId)
            .Select(s => s.ConversationId)
            .FirstOrDefaultAsync(ct);
        if (conversationId is null)
        {
            // Queued before stories were linked to their conversation: nowhere to report to.
            return;
        }

        var conversation = await db
            .Conversations.Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct);
        if (conversation is not { Status: ConversationStatus.Completed })
        {
            return;
        }

        var pageText = await db
            .Pages.Where(p => p.StoryId == storyId && p.PageNo == pageNo)
            .Select(p => p.Content)
            .FirstOrDefaultAsync(ct);

        Append(
            conversation,
            MessageRole.System,
            StoryAgentPrompt.GenerationFailedNote(pageNo, pageText ?? string.Empty, reason)
        );
        Append(conversation, MessageRole.Assistant, await AdviseAsync(conversation, ct));
        conversation.Status = ConversationStatus.Active;

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The agent's answer to the note, with the whole conversation in view. Stripped of the
    /// sentinel whatever the model wrote, because the sentinel starts generation and this message
    /// must never do that by itself.
    /// </summary>
    private async Task<string> AdviseAsync(Conversation conversation, CancellationToken ct)
    {
        var history = conversation
            .Messages.OrderBy(m => m.Seq)
            .Select(m => new ChatMessage(ChatRoles.ToChatRole(m.Role), m.Content))
            .ToList();

        ChatResponse response;
        try
        {
            response = await chatClient.GetResponseAsync(history, cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "The chat model could not write the advice for a failed story; using the fallback.");
            return FallbackAdvice;
        }

        if (response.FinishReason == ChatFinishReason.ContentFilter)
        {
            logger.LogWarning("The chat model's content filter stopped the advice for a failed story; using the fallback.");
            return FallbackAdvice;
        }

        var advice = response
            .Text.Replace(StoryAgentPrompt.EndOfChatSentinel, string.Empty, StringComparison.Ordinal)
            .Trim();
        return advice.Length > 0 ? advice : FallbackAdvice;
    }

    /// <summary>
    /// Appends a message after the last one. Added to the DbSet explicitly, as in
    /// <c>StoryAgent</c>, so EF inserts it rather than mistaking a client-set id for an update.
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
}
