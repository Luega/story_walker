using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using StoryWalker.Application.Agent;
using StoryWalker.Domain.Enums;
using StoryWalker.Infrastructure.Agent;

namespace StoryWalker.Api.Tests;

/// <summary>
/// Tests <see cref="StoryAgent"/>'s persistence against the real Postgres (the HTTP tests swap the
/// whole agent for a fake, so they don't exercise it). Drives the agent directly with a stub
/// <see cref="IChatClient"/>, each turn in its own DbContext scope to mimic separate requests.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class StoryAgentTests(StoryWalkerApiFactory factory)
{
    private readonly StoryWalkerApiFactory _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task First_turn_persists_system_then_user_then_assistant()
    {
        await _factory.ResetAsync();

        var id = await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(db, new StubChatClient { Reply = "Hi!" });
            var turn = await agent.SendAsync(conversationId: null, "hello", Ct);
            return turn.ConversationId;
        });

        await _factory.ExecuteDbAsync(async db =>
        {
            var conversation = await db
                .Conversations.Include(c => c.Messages)
                .SingleAsync(c => c.Id == Guid.Parse(id), Ct);

            Assert.Equal(ConversationStatus.Active, conversation.Status);
            var ordered = conversation.Messages.OrderBy(m => m.Seq).ToList();
            Assert.Equal(
                [MessageRole.System, MessageRole.User, MessageRole.Assistant],
                ordered.Select(m => m.Role).ToArray()
            );
            Assert.Equal(StoryAgentPrompt.Instructions, ordered[0].Content);
            Assert.Equal("hello", ordered[1].Content);
            Assert.Equal("Hi!", ordered[2].Content);
        });
    }

    [Fact]
    public async Task Second_turn_appends_to_the_same_conversation()
    {
        await _factory.ResetAsync();

        var id = await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(db, new StubChatClient());
            var turn = await agent.SendAsync(conversationId: null, "one", Ct);
            return turn.ConversationId;
        });

        await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(db, new StubChatClient());
            await agent.SendAsync(id, "two", Ct);
        });

        await _factory.ExecuteDbAsync(async db =>
        {
            var conversation = await db
                .Conversations.Include(c => c.Messages)
                .SingleAsync(c => c.Id == Guid.Parse(id), Ct);

            var ordered = conversation.Messages.OrderBy(m => m.Seq).ToList();
            // system, user("one"), assistant, user("two"), assistant — one conversation, one system.
            Assert.Equal(5, ordered.Count);
            Assert.Equal([0, 1, 2, 3, 4], ordered.Select(m => m.Seq).ToArray());
            Assert.Single(ordered, m => m.Role == MessageRole.System);
            Assert.Equal("one", ordered[1].Content);
            Assert.Equal("two", ordered[3].Content);
        });
    }

    [Fact]
    public async Task Unknown_conversation_id_throws_not_found()
    {
        await _factory.ResetAsync();

        await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(db, new StubChatClient());
            await Assert.ThrowsAsync<ConversationNotFoundException>(
                () => agent.SendAsync(Guid.NewGuid().ToString(), "hello?", Ct)
            );
        });
    }

    /// <summary>Returns a fixed assistant reply, ignoring the prompt.</summary>
    private sealed class StubChatClient : IChatClient
    {
        public string Reply { get; init; } = "ok";

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Reply)));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
