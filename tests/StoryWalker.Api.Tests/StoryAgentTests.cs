using System.ClientModel;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using StoryWalker.Application.Agent;
using StoryWalker.Application.Api;
using StoryWalker.Application.Brief;
using StoryWalker.Domain.Enums;
using StoryWalker.Infrastructure.Agent;
using StoryWalker.Infrastructure.Persistence;

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
            var turn = await agent.SendAsync(me, conversationId: null, "hello", ct: Ct);
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
            // This user saved no standing context, so the seeded prompt is the bare
            // instructions — byte for byte, with no empty context block bolted on.
            Assert.Equal(StoryAgentPrompt.Instructions, ordered[0].Content);
            Assert.Equal("hello", ordered[1].Content);
            Assert.Equal("Hi!", ordered[2].Content);
        });
    }

    [Fact]
    public async Task A_users_saved_context_is_composed_into_the_seeded_system_message()
    {
        await _factory.ResetAsync();
        var me = await _factory.ExecuteDbAsync(db =>
            SeedData.SeedUserAsync(
                db,
                "user_agent_context",
                new UserSettingsDto(
                    Kids: "Mia is 5 and loves dinosaurs",
                    Story: "Short, gentle, always a kind ending",
                    Characters: "Pip, a small grey kitten with green eyes",
                    ArtStyle: "soft watercolour"
                )
            )
        );

        var id = await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(db, new StubChatClient());
            var turn = await agent.SendAsync(me, conversationId: null, "hello", ct: Ct);
            return turn.ConversationId;
        });

        await _factory.ExecuteDbAsync(async db =>
        {
            var system = await FirstMessageAsync(db, id);

            Assert.StartsWith(StoryAgentPrompt.Instructions, system, StringComparison.Ordinal);
            Assert.Contains("Mia is 5 and loves dinosaurs", system, StringComparison.Ordinal);
            Assert.Contains("Short, gentle, always a kind ending", system, StringComparison.Ordinal);
            Assert.Contains("Pip, a small grey kitten", system, StringComparison.Ordinal);
            Assert.Contains("soft watercolour", system, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// The context is read once, when the conversation is created, and lives on as its first
    /// persisted message — so editing the settings page cannot move the ground under a chat that is
    /// already under way. This is the whole mechanism behind that guarantee, so it is pinned.
    /// </summary>
    [Fact]
    public async Task Editing_settings_does_not_change_a_conversation_already_under_way()
    {
        await _factory.ResetAsync();
        var me = await _factory.ExecuteDbAsync(db =>
            SeedData.SeedUserAsync(
                db,
                "user_agent_context_frozen",
                new UserSettingsDto("Mia is 5", null, null, "soft watercolour")
            )
        );

        var id = await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(db, new StubChatClient());
            var turn = await agent.SendAsync(me, conversationId: null, "one", ct: Ct);
            return turn.ConversationId;
        });

        await _factory.ExecuteDbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == me, Ct);
            user.KidsContext = "Leo is 7";
            user.ArtStyleContext = "crayon";
            await db.SaveChangesAsync(Ct);
        });

        await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(db, new StubChatClient());
            await agent.SendAsync(me, id, "two", ct: Ct);
        });

        await _factory.ExecuteDbAsync(async db =>
        {
            var system = await FirstMessageAsync(db, id);

            Assert.Contains("Mia is 5", system, StringComparison.Ordinal);
            Assert.Contains("soft watercolour", system, StringComparison.Ordinal);
            Assert.DoesNotContain("Leo is 7", system, StringComparison.Ordinal);
            Assert.DoesNotContain("crayon", system, StringComparison.Ordinal);
        });
    }

    /// <summary>A new conversation started afterwards does pick the edited context up.</summary>
    [Fact]
    public async Task A_conversation_started_after_an_edit_uses_the_new_context()
    {
        await _factory.ResetAsync();
        var me = await _factory.ExecuteDbAsync(db =>
            SeedData.SeedUserAsync(
                db,
                "user_agent_context_fresh",
                new UserSettingsDto("Mia is 5", null, null, null)
            )
        );

        await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(db, new StubChatClient());
            await agent.SendAsync(me, conversationId: null, "first story", ct: Ct);

            var user = await db.Users.SingleAsync(u => u.Id == me, Ct);
            user.KidsContext = "Leo is 7";
            await db.SaveChangesAsync(Ct);
        });

        var second = await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(db, new StubChatClient());
            var turn = await agent.SendAsync(me, conversationId: null, "second story", ct: Ct);
            return turn.ConversationId;
        });

        await _factory.ExecuteDbAsync(async db =>
        {
            var system = await FirstMessageAsync(db, second);

            Assert.Contains("Leo is 7", system, StringComparison.Ordinal);
            Assert.DoesNotContain("Mia is 5", system, StringComparison.Ordinal);
        });
    }

    /// <summary>Content of the conversation's first message, which is always the system prompt.</summary>
    private static async Task<string> FirstMessageAsync(
        StoryWalkerDbContext db,
        string conversationId
    )
    {
        var conversation = await db
            .Conversations.Include(c => c.Messages)
            .SingleAsync(c => c.Id == Guid.Parse(conversationId), Ct);

        return conversation.Messages.OrderBy(m => m.Seq).First().Content;
    }

    [Fact]
    public async Task Second_turn_appends_to_the_same_conversation()
    {
        await _factory.ResetAsync();
        var me = await _factory.ExecuteDbAsync(db => SeedData.SeedUserAsync(db, "user_agent_tests"));

        var id = await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(db, new StubChatClient());
            var turn = await agent.SendAsync(me, conversationId: null, "one", ct: Ct);
            return turn.ConversationId;
        });

        await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(db, new StubChatClient());
            await agent.SendAsync(me, id, "two", ct: Ct);
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
            turn = await agent.SendAsync(me, conversationId: null, "wrap it up", ct: Ct);
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
                () => agent.SendAsync(me, conversationId: null, "wrap it up", ct: Ct)
            );
        });

        // The turn was rolled back: nothing persisted, so no conversation exists.
        await _factory.ExecuteDbAsync(async db =>
            Assert.Equal(0, await db.Conversations.CountAsync(Ct))
        );
    }

    // What the OpenAI SDK throws when Azure's content filter refuses the prompt: a 400 whose error
    // code is content_filter, carried in the message it builds ("HTTP {status} ({type}: {code})").
    private const string ContentFilterError =
        "HTTP 400 (: content_filter)\n\nThe response was filtered due to the prompt triggering Azure OpenAI's content management policy.";

    [Fact]
    public async Task A_message_the_content_filter_refuses_throws_and_leaves_the_history_untouched()
    {
        await _factory.ResetAsync();
        var me = await _factory.ExecuteDbAsync(db => SeedData.SeedUserAsync(db, "user_agent_tests"));

        var id = await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(db, new StubChatClient());
            var turn = await agent.SendAsync(me, conversationId: null, "hello", ct: Ct);
            return turn.ConversationId;
        });

        await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(
                db,
                new StubChatClient { Throws = new ClientResultException(ContentFilterError) }
            );
            await Assert.ThrowsAsync<ContentFilteredException>(
                () => agent.SendAsync(me, id, "something unsuitable", ct: Ct)
            );
        });

        // Only the first turn survives: system, user, assistant. Had the refused message been
        // kept, every later turn would resend it and be refused again.
        await _factory.ExecuteDbAsync(async db =>
            Assert.Equal(
                3,
                await db.ConversationMessages.CountAsync(m => m.ConversationId == Guid.Parse(id), Ct)
            )
        );
    }

    [Fact]
    public async Task A_reply_the_content_filter_stops_throws_and_rolls_back_the_turn()
    {
        await _factory.ResetAsync();
        var me = await _factory.ExecuteDbAsync(db => SeedData.SeedUserAsync(db, "user_agent_tests"));

        await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(
                db,
                new StubChatClient { Reply = "", FinishReason = ChatFinishReason.ContentFilter }
            );
            await Assert.ThrowsAsync<ContentFilteredException>(
                () => agent.SendAsync(me, conversationId: null, "tell me a story", ct: Ct)
            );
        });

        await _factory.ExecuteDbAsync(async db =>
            Assert.Equal(0, await db.Conversations.CountAsync(Ct))
        );
    }

    [Fact]
    public async Task Other_model_errors_are_not_mistaken_for_a_content_filter_refusal()
    {
        await _factory.ResetAsync();
        var me = await _factory.ExecuteDbAsync(db => SeedData.SeedUserAsync(db, "user_agent_tests"));

        await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(
                db,
                new StubChatClient
                {
                    Throws = new ClientResultException(
                        "HTTP 400 (invalid_request_error: context_length_exceeded)"
                    ),
                }
            );
            await Assert.ThrowsAsync<ClientResultException>(
                () => agent.SendAsync(me, conversationId: null, "hello", ct: Ct)
            );
        });
    }

    // A related story (ADR-0029): the new conversation is seeded with the previous one.
    [Fact]
    public async Task A_conversation_based_on_a_story_is_seeded_with_it_and_remembers_it()
    {
        await _factory.ResetAsync();
        var me = await _factory.ExecuteDbAsync(db => SeedData.SeedUserAsync(db, "user_agent_tests"));
        var original = await _factory.ExecuteDbAsync(db => SeedData.SeedDemoStoryAsync(db, me));

        var id = await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(db, new StubChatClient());
            var turn = await agent.SendAsync(me, conversationId: null, "a sequel please", original, Ct);
            return turn.ConversationId;
        });

        await _factory.ExecuteDbAsync(async db =>
        {
            var conversation = await db
                .Conversations.Include(c => c.Messages)
                .SingleAsync(c => c.Id == Guid.Parse(id), Ct);
            Assert.Equal(original, conversation.BasedOnStoryId);
            var system = conversation.Messages.Single(m => m.Seq == 0).Content;
            Assert.StartsWith(StoryAgentPrompt.Instructions, system, StringComparison.Ordinal);
            Assert.Contains(StoryAgentPrompt.PreviousStoryStart, system, StringComparison.Ordinal);
            Assert.Contains("Luna and the Rainy Day Adventure", system, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Basing_a_conversation_on_someone_elses_story_is_not_found_and_saves_nothing()
    {
        await _factory.ResetAsync();
        var me = await _factory.ExecuteDbAsync(db => SeedData.SeedUserAsync(db, "user_agent_tests"));
        var stranger = await _factory.ExecuteDbAsync(db => SeedData.SeedUserAsync(db, "user_agent_stranger"));
        var theirs = await _factory.ExecuteDbAsync(db => SeedData.SeedDemoStoryAsync(db, stranger));

        await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(db, new StubChatClient());
            await Assert.ThrowsAsync<BasedOnStoryNotFoundException>(
                () => agent.SendAsync(me, conversationId: null, "a sequel", theirs, Ct)
            );
        });

        await _factory.ExecuteDbAsync(async db => Assert.Equal(0, await db.Conversations.CountAsync(Ct)));
    }

    // Only a finished story is something to build on.
    [Theory]
    [InlineData(StoryStatus.Generating)]
    [InlineData(StoryStatus.Failed)]
    public async Task Basing_a_conversation_on_an_unfinished_story_is_not_found(StoryStatus status)
    {
        await _factory.ResetAsync();
        var me = await _factory.ExecuteDbAsync(db => SeedData.SeedUserAsync(db, "user_agent_tests"));
        var original = await _factory.ExecuteDbAsync(async db =>
        {
            var id = await SeedData.SeedDemoStoryAsync(db, me);
            (await db.Stories.SingleAsync(s => s.Id == id, Ct)).Status = status;
            await db.SaveChangesAsync(Ct);
            return id;
        });

        await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(db, new StubChatClient());
            await Assert.ThrowsAsync<BasedOnStoryNotFoundException>(
                () => agent.SendAsync(me, conversationId: null, "a sequel", original, Ct)
            );
        });
    }

    [Fact]
    public async Task Basing_a_conversation_on_an_unknown_story_is_not_found()
    {
        await _factory.ResetAsync();
        var me = await _factory.ExecuteDbAsync(db => SeedData.SeedUserAsync(db, "user_agent_tests"));

        await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(db, new StubChatClient());
            await Assert.ThrowsAsync<BasedOnStoryNotFoundException>(
                () => agent.SendAsync(me, conversationId: null, "a sequel", Guid.NewGuid(), Ct)
            );
        });
    }

    // The authoring pass drops system messages, so without this the author would write the sequel
    // without ever seeing the characters it has to keep (ADR-0029).
    [Fact]
    public async Task The_brief_author_is_given_the_previous_story()
    {
        await _factory.ResetAsync();
        var me = await _factory.ExecuteDbAsync(db => SeedData.SeedUserAsync(db, "user_agent_tests"));
        var original = await _factory.ExecuteDbAsync(db => SeedData.SeedDemoStoryAsync(db, me));
        var chat = new StubChatClient { Reply = "Generating now. __END__CHAT__" };

        await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(db, chat);
            await agent.SendAsync(me, conversationId: null, "a sequel, go ahead", original, Ct);
        });

        var authoring = Assert.Single(chat.Requests, r => r.Authoring).Messages;
        Assert.Contains(
            authoring,
            m => m.Role == ChatRole.System && m.Text.Contains(StoryAgentPrompt.PreviousStoryStart, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task Continuing_a_conversation_ignores_a_story_to_base_it_on()
    {
        await _factory.ResetAsync();
        var me = await _factory.ExecuteDbAsync(db => SeedData.SeedUserAsync(db, "user_agent_tests"));
        var original = await _factory.ExecuteDbAsync(db => SeedData.SeedDemoStoryAsync(db, me));

        var id = await _factory.ExecuteDbAsync(async db =>
        {
            var turn = await new StoryAgent(db, new StubChatClient()).SendAsync(me, conversationId: null, "hi", ct: Ct);
            return turn.ConversationId;
        });
        await _factory.ExecuteDbAsync(db =>
            new StoryAgent(db, new StubChatClient()).SendAsync(me, id, "go on", original, Ct)
        );

        var basedOn = await _factory.ExecuteDbAsync(db =>
            db.Conversations.Where(c => c.Id == Guid.Parse(id)).Select(c => c.BasedOnStoryId).SingleAsync(Ct)
        );
        Assert.Null(basedOn);
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
            var turn = await agent.SendAsync(me, conversationId: null, "wrap it up", ct: Ct);
            id = turn.ConversationId;
        });

        AgentTurnResult turn = null!;
        await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(db, new StubChatClient { Reply = "should not be called" });
            turn = await agent.SendAsync(me, id, "anything else?", ct: Ct);
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
                () => agent.SendAsync(me, Guid.NewGuid().ToString(), "hello?", ct: Ct)
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
            var turn = await agent.SendAsync(owner, conversationId: null, "hello", ct: Ct);
            return turn.ConversationId;
        });

        await _factory.ExecuteDbAsync(async db =>
        {
            var agent = new StoryAgent(db, new StubChatClient());
            await Assert.ThrowsAsync<ConversationNotFoundException>(
                () => agent.SendAsync(stranger, id, "let me in", ct: Ct)
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

        /// <summary>When set, every call throws this, as the SDK does for an error response.</summary>
        public Exception? Throws { get; init; }

        /// <summary>The finish reason reported on every response; null, as most providers leave it.</summary>
        public ChatFinishReason? FinishReason { get; init; }

        /// <summary>Every request, with whether it was the structured-output authoring call.</summary>
        public List<(IReadOnlyList<ChatMessage> Messages, bool Authoring)> Requests { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            Requests.Add(([.. messages], options?.ResponseFormat is ChatResponseFormatJson));

            if (Throws is not null)
            {
                throw Throws;
            }

            var text =
                options?.ResponseFormat is ChatResponseFormatJson
                    ? JsonSerializer.Serialize(Brief, AIJsonUtilities.DefaultOptions)
                    : Reply;
            return Task.FromResult(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, text)) { FinishReason = FinishReason }
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
}
