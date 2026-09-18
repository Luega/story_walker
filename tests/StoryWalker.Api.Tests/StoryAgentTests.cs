using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using StoryWalker.Application.Agent;
using StoryWalker.Application.Brief;
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
        var me = await _factory.ExecuteDbAsync(db => SeedData.SeedUserAsync(db, "user_agent_tests"));

        var id = await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(db, new StubChatClient { Reply = "Hi!" });
            var turn = await agent.SendAsync(me, conversationId: null, "hello", Ct);
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
        var me = await _factory.ExecuteDbAsync(db => SeedData.SeedUserAsync(db, "user_agent_tests"));

        var id = await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(db, new StubChatClient());
            var turn = await agent.SendAsync(me, conversationId: null, "one", Ct);
            return turn.ConversationId;
        });

        await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(db, new StubChatClient());
            await agent.SendAsync(me, id, "two", Ct);
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
    public async Task End_chat_sentinel_completes_the_conversation()
    {
        await _factory.ResetAsync();
        var me = await _factory.ExecuteDbAsync(db => SeedData.SeedUserAsync(db, "user_agent_tests"));

        string id = null!;
        AgentTurnResult turn = null!;
        await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(db, new StubChatClient { Reply = "Here is your brief. __END__CHAT__" });
            turn = await agent.SendAsync(me, conversationId: null, "wrap it up", Ct);
            id = turn.ConversationId;
        });

        Assert.True(turn.BriefReady);
        // The confirmation turn also authors the brief (structured-output call).
        Assert.NotNull(turn.Brief);
        Assert.Equal("Luna and the Rainy Day", turn.Brief.Title);
        Assert.Equal(5, turn.Brief.TargetAge);
        Assert.Equal(GenerationBriefValidator.PageCount, turn.Brief.Pages.Count);

        await _factory.ExecuteDbAsync(async db =>
        {
            var conversation = await db.Conversations.SingleAsync(c => c.Id == Guid.Parse(id), Ct);
            Assert.Equal(ConversationStatus.Completed, conversation.Status);
        });
    }

    [Fact]
    public async Task Invalid_brief_throws_and_rolls_back_the_turn()
    {
        await _factory.ResetAsync();
        var me = await _factory.ExecuteDbAsync(db => SeedData.SeedUserAsync(db, "user_agent_tests"));

        // A brief with no pages fails validation; after the retry the agent gives up.
        var invalid = new GenerationBrief(
            Title: "No pages",
            TargetAge: 5,
            ArtStyle: "watercolour",
            Pages: []
        );

        await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(
                db,
                new StubChatClient { Reply = "Generating now. __END__CHAT__", Brief = invalid }
            );
            await Assert.ThrowsAsync<BriefValidationException>(
                () => agent.SendAsync(me, conversationId: null, "wrap it up", Ct)
            );
        });

        // The turn was rolled back: nothing persisted, so no conversation exists.
        await _factory.ExecuteDbAsync(async db =>
            Assert.Equal(0, await db.Conversations.CountAsync(Ct))
        );
    }

    [Fact]
    public async Task Turn_on_completed_conversation_returns_already_completed_without_appending()
    {
        await _factory.ResetAsync();
        var me = await _factory.ExecuteDbAsync(db => SeedData.SeedUserAsync(db, "user_agent_tests"));

        string id = null!;
        await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(db, new StubChatClient { Reply = "Here is your brief. __END__CHAT__" });
            var turn = await agent.SendAsync(me, conversationId: null, "wrap it up", Ct);
            id = turn.ConversationId;
        });

        AgentTurnResult turn = null!;
        await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(db, new StubChatClient { Reply = "should not be called" });
            turn = await agent.SendAsync(me, id, "anything else?", Ct);
        });

        Assert.Equal(id, turn.ConversationId);
        Assert.True(turn.BriefReady);
        Assert.Equal(
            "The conversation has already completed. Wait for the story generation to finish, or start a new conversation.",
            turn.Reply
        );

        await _factory.ExecuteDbAsync(async db =>
        {
            var conversation = await db
                .Conversations.Include(c => c.Messages)
                .SingleAsync(c => c.Id == Guid.Parse(id), Ct);

            // The early return must not append the new user turn nor change the completed status.
            Assert.Equal(ConversationStatus.Completed, conversation.Status);
            // system, user("wrap it up"), assistant — the second turn was rejected before appending.
            Assert.Equal(3, conversation.Messages.Count);
        });
    }

    [Fact]
    public async Task Unknown_conversation_id_throws_not_found()
    {
        await _factory.ResetAsync();
        var me = await _factory.ExecuteDbAsync(db => SeedData.SeedUserAsync(db, "user_agent_tests"));

        await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(db, new StubChatClient());
            await Assert.ThrowsAsync<ConversationNotFoundException>(
                () => agent.SendAsync(me, Guid.NewGuid().ToString(), "hello?", Ct)
            );
        });
    }

    [Fact]
    public async Task Another_users_conversation_id_throws_not_found_and_appends_nothing()
    {
        // The HTTP test for this runs against FakeStoryAgent; this is the one that exercises the
        // real owner filter in LoadConversationAsync, which is where the rule is actually enforced.
        await _factory.ResetAsync();
        var owner = await _factory.ExecuteDbAsync(db => SeedData.SeedUserAsync(db, "user_agent_owner"));
        var stranger = await _factory.ExecuteDbAsync(db =>
            SeedData.SeedUserAsync(db, "user_agent_stranger")
        );

        var id = await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(db, new StubChatClient());
            var turn = await agent.SendAsync(owner, conversationId: null, "hello", Ct);
            return turn.ConversationId;
        });

        await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(db, new StubChatClient());
            await Assert.ThrowsAsync<ConversationNotFoundException>(
                () => agent.SendAsync(stranger, id, "let me in", Ct)
            );
        });

        // The attempt left the owner's conversation untouched: system, user, assistant.
        await _factory.ExecuteDbAsync(async db =>
            Assert.Equal(
                3,
                await db.ConversationMessages.CountAsync(m => m.ConversationId == Guid.Parse(id), Ct)
            )
        );
    }

    private static readonly GenerationBrief ValidBrief = new(
        Title: "Luna and the Rainy Day",
        TargetAge: 5,
        ArtStyle: "watercolour, soft pastels",
        Pages:
        [
            .. Enumerable
                .Range(1, GenerationBriefValidator.PageCount)
                .Select(n => new BriefPage(
                    n,
                    $"Luna woke to the rain. ({n})",
                    "Luna, a round-faced girl in a yellow raincoat, at a rainy window"
                )),
        ]
    );

    /// <summary>
    /// Returns a fixed conversational reply, except on the structured-output authoring call
    /// (<see cref="ChatResponseFormatJson"/>), where it returns a serialized <see cref="GenerationBrief"/>
    /// so <c>GetResponseAsync&lt;GenerationBrief&gt;</c> can deserialize it.
    /// </summary>
    private sealed class StubChatClient : IChatClient
    {
        public string Reply { get; init; } = "ok";

        /// <summary>The brief JSON returned on the authoring call; a valid one by default.</summary>
        public GenerationBrief Brief { get; init; } = ValidBrief;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            var text =
                options?.ResponseFormat is ChatResponseFormatJson
                    ? JsonSerializer.Serialize(Brief, AIJsonUtilities.DefaultOptions)
                    : Reply;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
