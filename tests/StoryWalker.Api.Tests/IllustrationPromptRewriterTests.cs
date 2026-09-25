using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using StoryWalker.Application.Images;
using StoryWalker.Infrastructure.Generation;

namespace StoryWalker.Api.Tests;

/// <summary>
/// Unit tests for <see cref="IllustrationPromptRewriter"/>, which asks the chat model for a gentler
/// wording of an illustration prompt the image model refused (ADR-0025). No database and no model:
/// what matters is what it sends, and that every way the rewrite can fail comes back as null, so the
/// renderer fails the page instead of the whole story.
/// </summary>
public sealed class IllustrationPromptRewriterTests
{
    private const string RefusedPrompt =
        "George, a small white rabbit in blue dungarees, holds scissors next to the hat with paper ears.";

    private const string PageText = "George picked up the scissors. Picu-Picu stopped him just in time.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IllustrationPromptRewriter NewRewriter(IChatClient chat) =>
        new(chat, NullLogger<IllustrationPromptRewriter>.Instance);

    [Fact]
    public async Task Returns_the_models_rewrite_trimmed()
    {
        var chat = new RecordingChatClient { Reply = "  George looks at the hat; the scissors lie on the table.\n" };

        var rewritten = await NewRewriter(chat).RewriteAsync(RefusedPrompt, PageText, 6, Ct);

        Assert.Equal("George looks at the hat; the scissors lie on the table.", rewritten);
    }

    [Fact]
    public async Task Sends_the_rewrite_instructions_as_the_system_message()
    {
        var chat = new RecordingChatClient();

        await NewRewriter(chat).RewriteAsync(RefusedPrompt, PageText, 6, Ct);

        var request = Assert.Single(chat.Requests);
        Assert.Equal(ChatRole.System, request[0].Role);
        Assert.Equal(IllustrationRewritePrompt.Instructions, request[0].Text);
    }

    // The page text is the anchor that keeps the picture on the same moment of the story, and the
    // refused prompt carries the character descriptions that have to survive word for word.
    [Fact]
    public async Task Sends_the_refused_prompt_the_page_text_and_the_age()
    {
        var chat = new RecordingChatClient();

        await NewRewriter(chat).RewriteAsync(RefusedPrompt, PageText, 6, Ct);

        var user = Assert.Single(chat.Requests)[^1];
        Assert.Equal(ChatRole.User, user.Role);
        Assert.Contains(RefusedPrompt, user.Text, StringComparison.Ordinal);
        Assert.Contains(PageText, user.Text, StringComparison.Ordinal);
        Assert.Contains("6", user.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n")]
    public async Task A_blank_reply_is_no_rewrite(string reply)
    {
        var chat = new RecordingChatClient { Reply = reply };

        Assert.Null(await NewRewriter(chat).RewriteAsync(RefusedPrompt, PageText, 6, Ct));
    }

    [Fact]
    public async Task A_reply_the_content_filter_stopped_is_no_rewrite()
    {
        var chat = new RecordingChatClient { Reply = "George holds", FinishReason = ChatFinishReason.ContentFilter };

        Assert.Null(await NewRewriter(chat).RewriteAsync(RefusedPrompt, PageText, 6, Ct));
    }

    [Fact]
    public async Task A_failed_chat_call_is_no_rewrite()
    {
        var chat = new RecordingChatClient { Throws = new ClientResultException("HTTP 429 (rate_limit_exceeded)") };

        Assert.Null(await NewRewriter(chat).RewriteAsync(RefusedPrompt, PageText, 6, Ct));
    }

    // Cancellation is the host shutting down or the invocation timing out; swallowing it would
    // carry on rendering against a token that has already fired.
    [Fact]
    public async Task Cancellation_is_not_swallowed()
    {
        var chat = new RecordingChatClient { Throws = new OperationCanceledException() };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => NewRewriter(chat).RewriteAsync(RefusedPrompt, PageText, 6, Ct)
        );
    }
}
