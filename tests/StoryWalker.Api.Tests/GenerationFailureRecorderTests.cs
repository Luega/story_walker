using System.ClientModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using StoryWalker.Application.Agent;
using StoryWalker.Application.Brief;
using StoryWalker.Domain.Entities;
using StoryWalker.Domain.Enums;
using StoryWalker.Infrastructure.Generation;
using StoryWalker.Infrastructure.Persistence;

namespace StoryWalker.Api.Tests;

/// <summary>
/// Tests <see cref="GenerationFailureRecorder"/> against the real Postgres: when a story fails, its
/// conversation gets a hidden system note and the agent's advice, and goes back to Active so it can
/// be resumed (ADR-0027). The chat model is a <see cref="RecordingChatClient"/>.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class GenerationFailureRecorderTests(StoryWalkerApiFactory factory)
{
    private const string Advice = "Non sono riuscito a illustrare la pagina 3. Proviamo con le forbici posate sul tavolo?";

    private readonly StoryWalkerApiFactory _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Appends_a_system_note_then_the_agents_advice_and_reopens_the_conversation()
    {
        var (storyId, conversationId) = await SeedFailedStoryAsync();

        await RecordAsync(storyId, new RecordingChatClient { Reply = Advice });

        var messages = await MessagesAsync(conversationId);
        Assert.Equal(5, messages.Count);
        Assert.Equal(MessageRole.System, messages[3].Role);
        Assert.Contains("page 3", messages[3].Content, StringComparison.Ordinal);
        Assert.Contains("Page 3 text.", messages[3].Content, StringComparison.Ordinal);
        Assert.Equal(MessageRole.Assistant, messages[4].Role);
        Assert.Equal(Advice, messages[4].Content);
        Assert.Equal(ConversationStatus.Active, await ConversationStatusAsync(conversationId));
    }

    // The agent answers with the whole conversation in view, so its advice can refer to the plan the
    // parent confirmed, with the note last as the thing it is answering.
    [Fact]
    public async Task Asks_the_model_with_the_whole_conversation_and_the_note_last()
    {
        var (storyId, _) = await SeedFailedStoryAsync();
        var chat = new RecordingChatClient { Reply = Advice };

        await RecordAsync(storyId, chat);

        var request = Assert.Single(chat.Requests);
        Assert.Equal(4, request.Count);
        Assert.Equal("A story about George and the scissors, please.", request[1].Text);
        Assert.Equal(ChatRole.System, request[^1].Role);
        Assert.Contains("GENERATION FAILED", request[^1].Text, StringComparison.Ordinal);
    }

    // The sentinel starts generation. The advice must never do that by itself, whatever the model
    // wrote.
    [Fact]
    public async Task Strips_the_sentinel_from_the_advice()
    {
        var (storyId, conversationId) = await SeedFailedStoryAsync();

        await RecordAsync(
            storyId,
            new RecordingChatClient { Reply = $"{Advice} {StoryAgentPrompt.EndOfChatSentinel}" }
        );

        Assert.Equal(Advice, (await MessagesAsync(conversationId))[^1].Content);
    }

    [Fact]
    public async Task Falls_back_to_a_fixed_sentence_when_the_model_cannot_answer()
    {
        var (storyId, conversationId) = await SeedFailedStoryAsync();

        await RecordAsync(
            storyId,
            new RecordingChatClient { Throws = new ClientResultException("HTTP 429 (rate_limit_exceeded)") }
        );

        var messages = await MessagesAsync(conversationId);
        Assert.Equal(GenerationFailureRecorder.FallbackAdvice, messages[^1].Content);
        Assert.Equal(MessageRole.Assistant, messages[^1].Role);
        Assert.Equal(ConversationStatus.Active, await ConversationStatusAsync(conversationId));
    }

    [Fact]
    public async Task Falls_back_when_the_model_answers_with_nothing_usable()
    {
        var (storyId, conversationId) = await SeedFailedStoryAsync();

        // Only the sentinel: nothing left once it is stripped.
        await RecordAsync(storyId, new RecordingChatClient { Reply = StoryAgentPrompt.EndOfChatSentinel });

        Assert.Equal(GenerationFailureRecorder.FallbackAdvice, (await MessagesAsync(conversationId))[^1].Content);
    }

    // Stories queued before the link existed have nowhere to report to.
    [Fact]
    public async Task Does_nothing_for_a_story_without_a_conversation()
    {
        var (storyId, conversationId) = await SeedFailedStoryAsync(linked: false);
        var chat = new RecordingChatClient { Reply = Advice };

        await RecordAsync(storyId, chat);

        Assert.Empty(chat.Requests);
        Assert.Equal(3, (await MessagesAsync(conversationId)).Count);
    }

    // A redelivered message runs the worker again; the conversation must not get the report twice.
    [Fact]
    public async Task Records_only_once()
    {
        var (storyId, conversationId) = await SeedFailedStoryAsync();
        var chat = new RecordingChatClient { Reply = Advice };

        await RecordAsync(storyId, chat);
        await RecordAsync(storyId, chat);

        Assert.Single(chat.Requests);
        Assert.Equal(5, (await MessagesAsync(conversationId)).Count);
    }

    private Task RecordAsync(Guid storyId, RecordingChatClient chat) =>
        _factory.ExecuteDbAsync(db =>
            new GenerationFailureRecorder(db, chat, NullLogger<GenerationFailureRecorder>.Instance)
                .RecordAsync(storyId, 3, GenerationFailureReason.ImageRefused, Ct)
        );

    /// <summary>
    /// A confirmed conversation (system, user, the agent's "generating now" with the sentinel) and
    /// the Failed story it queued, with its six pages.
    /// </summary>
    private async Task<(Guid StoryId, Guid ConversationId)> SeedFailedStoryAsync(bool linked = true)
    {
        await _factory.ResetAsync();
        var userId = await _factory.ExecuteDbAsync(db => SeedData.SeedUserAsync(db, "user_failure_tests"));
        var conversationId = Guid.NewGuid();
        var storyId = Guid.NewGuid();

        await _factory.ExecuteDbAsync(async db =>
        {
            var conversation = new Conversation
            {
                Id = conversationId,
                UserId = userId,
                Status = ConversationStatus.Completed,
            };
            db.Conversations.Add(conversation);
            string[] contents =
            [
                StoryAgentPrompt.Instructions,
                "A story about George and the scissors, please.",
                $"Generating your story now. {StoryAgentPrompt.EndOfChatSentinel}",
            ];
            MessageRole[] roles = [MessageRole.System, MessageRole.User, MessageRole.Assistant];
            for (var i = 0; i < contents.Length; i++)
            {
                db.ConversationMessages.Add(
                    new ConversationMessage
                    {
                        Id = Guid.NewGuid(),
                        ConversationId = conversationId,
                        Seq = i,
                        Role = roles[i],
                        Content = contents[i],
                    }
                );
            }

            db.Stories.Add(
                new Story
                {
                    Id = storyId,
                    UserId = userId,
                    ConversationId = linked ? conversationId : null,
                    Title = "George and the Paper Ears",
                    TargetAge = 6,
                    ArtStyle = "soft watercolour",
                    Status = StoryStatus.Failed,
                }
            );
            await db.SaveChangesAsync(Ct);

            await new StoryPersister(db).PersistPagesAsync(storyId, Brief(), Ct);
        });

        return (storyId, conversationId);
    }

    private static GenerationBrief Brief() =>
        new(
            Title: "George and the Paper Ears",
            TargetAge: 6,
            ArtStyle: "soft watercolour",
            Pages:
            [
                .. Enumerable
                    .Range(1, GenerationBriefValidator.PageCount)
                    .Select(n => new BriefPage(n, $"Page {n} text.", $"George, a small white rabbit, scene {n}")),
            ]
        );

    private Task<List<ConversationMessage>> MessagesAsync(Guid conversationId) =>
        _factory.ExecuteDbAsync(db =>
            db.ConversationMessages.Where(m => m.ConversationId == conversationId).OrderBy(m => m.Seq).ToListAsync(Ct)
        );

    private Task<ConversationStatus> ConversationStatusAsync(Guid conversationId) =>
        _factory.ExecuteDbAsync(db =>
            db.Conversations.Where(c => c.Id == conversationId).Select(c => c.Status).SingleAsync(Ct)
        );
}
