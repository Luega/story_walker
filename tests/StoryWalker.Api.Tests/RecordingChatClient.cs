using Microsoft.Extensions.AI;

namespace StoryWalker.Api.Tests;

/// <summary>
/// Test double for <see cref="IChatClient"/>: records every request it is sent and answers with a
/// fixed reply, or throws. Used where a service makes a single plain-text call to the chat model,
/// such as the illustration prompt rewrite (ADR-0025).
/// </summary>
internal sealed class RecordingChatClient : IChatClient
{
    public string Reply { get; init; } = "ok";

    /// <summary>When set, every call throws this, as the SDK does for an error response.</summary>
    public Exception? Throws { get; init; }

    /// <summary>The finish reason reported on every response; null, as most providers leave it.</summary>
    public ChatFinishReason? FinishReason { get; init; }

    public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        Requests.Add([.. messages]);

        if (Throws is not null)
        {
            throw Throws;
        }

        return Task.FromResult(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, Reply)) { FinishReason = FinishReason }
        );
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
