using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using StoryWalker.Application.Images;

namespace StoryWalker.Infrastructure.Generation;

/// <summary>
/// Asks the chat model for a gentler wording of an illustration prompt the image model refused on
/// moderation grounds (ADR-0025). It rewrites the prompt only: the page text goes in as the anchor
/// the picture has to keep matching, and is never changed.
/// <para>
/// Every way the rewrite can fail (an error, a blank reply, a reply the chat model's own filter
/// stopped) comes back as <c>null</c>. The rewrite is a second chance for one page, so a failure
/// here leaves that page <c>Failed</c>, exactly as it was before the rewrite existed.
/// </para>
/// </summary>
public sealed class IllustrationPromptRewriter(
    IChatClient chatClient,
    ILogger<IllustrationPromptRewriter> logger
)
{
    /// <returns>The rewritten prompt, or <c>null</c> when there is no usable rewrite.</returns>
    public async Task<string?> RewriteAsync(
        string illustrationPrompt,
        string pageText,
        int targetAge,
        CancellationToken ct = default
    )
    {
        List<ChatMessage> messages =
        [
            new(ChatRole.System, IllustrationRewritePrompt.Instructions),
            new(
                ChatRole.User,
                $"""
                The child is {targetAge} years old.

                Page text:
                {pageText}

                Refused illustration prompt:
                {illustrationPrompt}
                """
            ),
        ];

        ChatResponse response;
        try
        {
            response = await chatClient.GetResponseAsync(messages, cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "The chat model could not rewrite a refused illustration prompt.");
            return null;
        }

        if (response.FinishReason == ChatFinishReason.ContentFilter)
        {
            logger.LogWarning("The chat model's content filter stopped the illustration prompt rewrite.");
            return null;
        }

        var rewritten = response.Text.Trim();
        return rewritten.Length > 0 ? rewritten : null;
    }
}
