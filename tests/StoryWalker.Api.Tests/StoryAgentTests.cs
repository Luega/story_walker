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
    public async Task End_chat_sentinel_completes_the_conversation()
    {
        await _factory.ResetAsync();

        string id = null!;
        AgentTurnResult turn = null!;
        await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(db, new StubChatClient { Reply = "Here is your brief. __END__CHAT__" });
            turn = await agent.SendAsync(conversationId: null, "wrap it up", Ct);
            id = turn.ConversationId;
        });

        Assert.True(turn.BriefReady);
        // The confirmation turn also authors the brief (structured-output call).
        Assert.NotNull(turn.Brief);
        Assert.Equal("Luna and the Rainy Day", turn.Brief.Story.Title);
        Assert.Null(turn.Brief.Story.BasedOnStoryId);
        Assert.Single(turn.Brief.Pages);
        Assert.All(turn.Brief.Characters, c => Assert.Null(c.ExistingCharacterId));

        await _factory.ExecuteDbAsync(async db =>
        {
            var conversation = await db.Conversations.SingleAsync(c => c.Id == Guid.Parse(id), Ct);
            Assert.Equal(ConversationStatus.Completed, conversation.Status);
        });
    }

    [Fact]
    public async Task Reuse_links_are_forced_null_even_if_the_model_sets_them()
    {
        await _factory.ResetAsync();

        // The model returns a brief that (wrongly) reuses an existing story and character; the
        // agent must blank those links while reuse is deferred (ADR-0009).
        var reusing = new GenerationBrief(
            new BriefStory("Reused", BasedOnStoryId: Guid.NewGuid()),
            "watercolour",
            "soft pastels",
            [new BriefCharacter("Luna", "a girl", "Luna, a girl", ExistingCharacterId: Guid.NewGuid())],
            [new BriefPage(1, "text", "prompt", ["Luna"])]
        );

        AgentTurnResult turn = null!;
        await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(
                db,
                new StubChatClient { Reply = "Generating now. __END__CHAT__", Brief = reusing }
            );
            turn = await agent.SendAsync(conversationId: null, "wrap it up", Ct);
        });

        Assert.NotNull(turn.Brief);
        Assert.Null(turn.Brief.Story.BasedOnStoryId);
        Assert.All(turn.Brief.Characters, c => Assert.Null(c.ExistingCharacterId));
    }

    [Fact]
    public async Task Invalid_brief_throws_and_rolls_back_the_turn()
    {
        await _factory.ResetAsync();

        // A brief with no pages fails validation; after the retry the agent gives up.
        var invalid = new GenerationBrief(
            new BriefStory("No pages", BasedOnStoryId: null),
            "watercolour",
            "soft pastels",
            [new BriefCharacter("Luna", "a girl", "Luna, a girl", ExistingCharacterId: null)],
            Pages: []
        );

        await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(
                db,
                new StubChatClient { Reply = "Generating now. __END__CHAT__", Brief = invalid }
            );
            await Assert.ThrowsAsync<BriefValidationException>(
                () => agent.SendAsync(conversationId: null, "wrap it up", Ct)
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

        string id = null!;
        await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(db, new StubChatClient { Reply = "Here is your brief. __END__CHAT__" });
            var turn = await agent.SendAsync(conversationId: null, "wrap it up", Ct);
            id = turn.ConversationId;
        });

        AgentTurnResult turn = null!;
        await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(db, new StubChatClient { Reply = "should not be called" });
            turn = await agent.SendAsync(id, "anything else?", Ct);
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

        await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(db, new StubChatClient());
            await Assert.ThrowsAsync<ConversationNotFoundException>(
                () => agent.SendAsync(Guid.NewGuid().ToString(), "hello?", Ct)
            );
        });
    }

    private static readonly GenerationBrief ValidBrief = new(
        new BriefStory("Luna and the Rainy Day", BasedOnStoryId: null),
        ArtStyle: "watercolour",
        Palette: "soft pastels",
        Characters: [new BriefCharacter("Luna", "round-faced girl in a yellow raincoat", "Luna, a round-faced girl in a yellow raincoat", ExistingCharacterId: null)],
        Pages: [new BriefPage(1, "Luna woke to the rain.", "Luna at a rainy window", ["Luna"])]
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
