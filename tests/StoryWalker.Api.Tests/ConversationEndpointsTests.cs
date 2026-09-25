using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using StoryWalker.Application.Agent;
using StoryWalker.Application.Api;
using StoryWalker.Domain.Entities;
using StoryWalker.Domain.Enums;

namespace StoryWalker.Api.Tests;

/// <summary>
/// End-to-end tests for the conversation history endpoints (ADR-0028), over HTTP against a real
/// Postgres. Conversations are seeded directly: the agent that normally writes them is a fake here.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ConversationEndpointsTests(StoryWalkerApiFactory factory)
{
    private const string Me = "user_conversation_tests";

    private readonly StoryWalkerApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClientAs(Me);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task List_puts_the_most_recent_activity_first()
    {
        var me = await ResetAndSeedMeAsync();
        var older = await SeedConversationAsync(me, ConversationStatus.Active, messages: [(MessageRole.User, "older")]);
        var newer = await SeedConversationAsync(me, ConversationStatus.Active, messages: [(MessageRole.User, "newer")]);
        // Created first, but written to most recently.
        var revived = await SeedConversationAsync(me, ConversationStatus.Active, messages: [(MessageRole.User, "revived")]);
        await AgeMessagesAsync(older, TimeSpan.FromDays(3));
        await AgeMessagesAsync(newer, TimeSpan.FromDays(2));
        await AgeMessagesAsync(revived, TimeSpan.FromDays(5));
        await AddMessageAsync(revived, MessageRole.User, "back again");

        var list = await ListAsync();

        Assert.Equal([revived, newer, older], list.Select(c => c.Id).ToArray());
    }

    [Fact]
    public async Task Title_is_the_users_own_when_they_chose_one()
    {
        var me = await ResetAndSeedMeAsync();
        var id = await SeedConversationAsync(me, ConversationStatus.Completed, title: "Bedtime ideas", messages: [(MessageRole.User, "hi")]);
        await LinkStoryAsync(id, me, "The Fox and the Moon", StoryStatus.Complete);

        Assert.Equal("Bedtime ideas", Assert.Single(await ListAsync()).Title);
    }

    [Fact]
    public async Task Title_defaults_to_the_most_recent_story_queued_from_it()
    {
        var me = await ResetAndSeedMeAsync();
        var id = await SeedConversationAsync(me, ConversationStatus.Completed, messages: [(MessageRole.User, "hi")]);
        await LinkStoryAsync(id, me, "First try", StoryStatus.Failed);
        await LinkStoryAsync(id, me, "Second try", StoryStatus.Complete);

        Assert.Equal("Second try", Assert.Single(await ListAsync()).Title);
    }

    [Fact]
    public async Task Title_falls_back_to_the_start_of_the_first_user_message()
    {
        var me = await ResetAndSeedMeAsync();
        var first = "A story about a brave little rabbit called George who wants to cut his paper ears";
        await SeedConversationAsync(
            me,
            ConversationStatus.Active,
            messages: [(MessageRole.System, "instructions"), (MessageRole.User, first), (MessageRole.User, "second")]
        );

        var title = Assert.Single(await ListAsync()).Title;

        Assert.Equal(first[..60].TrimEnd() + "…", title);
    }

    [Fact]
    public async Task Flags_what_can_be_resumed_and_what_failed()
    {
        var me = await ResetAndSeedMeAsync();
        var inProgress = await SeedConversationAsync(me, ConversationStatus.Active, messages: [(MessageRole.User, "a")]);
        var failed = await SeedConversationAsync(me, ConversationStatus.Active, messages: [(MessageRole.User, "b")]);
        await LinkStoryAsync(failed, me, "Gone wrong", StoryStatus.Failed);
        var done = await SeedConversationAsync(me, ConversationStatus.Completed, messages: [(MessageRole.User, "c")]);
        await LinkStoryAsync(done, me, "All well", StoryStatus.Complete);

        var list = (await ListAsync()).ToDictionary(c => c.Id);

        Assert.True(list[inProgress].Resumable);
        Assert.False(list[inProgress].StoryFailed);
        Assert.True(list[failed].Resumable);
        Assert.True(list[failed].StoryFailed);
        Assert.False(list[done].Resumable);
        Assert.False(list[done].StoryFailed);
        Assert.Equal("Completed", list[done].Status);
    }

    [Fact]
    public async Task Another_users_conversations_are_not_listed()
    {
        await ResetAndSeedMeAsync();
        var stranger = await _factory.ExecuteDbAsync(db => SeedData.SeedUserAsync(db, "user_conversation_stranger"));
        await SeedConversationAsync(stranger, ConversationStatus.Active, messages: [(MessageRole.User, "not yours")]);

        Assert.Empty(await ListAsync());
    }

    private async Task<List<ConversationSummaryDto>> ListAsync()
    {
        var response = await _client.GetAsync("/stories/conversations", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<ConversationSummaryDto>>(Ct))!;
    }

    private async Task<Guid> ResetAndSeedMeAsync()
    {
        await _factory.ResetAsync();
        return await _factory.ExecuteDbAsync(db => SeedData.SeedUserAsync(db, Me));
    }

    private Task<Guid> SeedConversationAsync(
        Guid userId,
        ConversationStatus status,
        string? title = null,
        params (MessageRole Role, string Content)[] messages
    ) =>
        _factory.ExecuteDbAsync(async db =>
        {
            var conversation = new Conversation
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Status = status,
                Title = title,
            };
            db.Conversations.Add(conversation);
            for (var i = 0; i < messages.Length; i++)
            {
                db.ConversationMessages.Add(
                    new ConversationMessage
                    {
                        Id = Guid.NewGuid(),
                        ConversationId = conversation.Id,
                        Seq = i,
                        Role = messages[i].Role,
                        Content = messages[i].Content,
                    }
                );
            }
            await db.SaveChangesAsync(Ct);
            return conversation.Id;
        });

    private Task AddMessageAsync(Guid conversationId, MessageRole role, string content) =>
        _factory.ExecuteDbAsync(async db =>
        {
            var seq = await db.ConversationMessages.CountAsync(m => m.ConversationId == conversationId, Ct);
            db.ConversationMessages.Add(
                new ConversationMessage
                {
                    Id = Guid.NewGuid(),
                    ConversationId = conversationId,
                    Seq = seq,
                    Role = role,
                    Content = content,
                }
            );
            await db.SaveChangesAsync(Ct);
        });

    /// <summary>
    /// Moves a conversation's messages into the past. SaveChanges stamps CreatedAt with the current
    /// time, so ordering tests set it afterwards, straight in the database.
    /// </summary>
    private Task AgeMessagesAsync(Guid conversationId, TimeSpan age) =>
        _factory.ExecuteDbAsync(db =>
            db.ConversationMessages.Where(m => m.ConversationId == conversationId)
                .ExecuteUpdateAsync(set => set.SetProperty(m => m.CreatedAt, DateTime.UtcNow - age), Ct)
        );

    private Task<Guid> LinkStoryAsync(Guid conversationId, Guid userId, string title, StoryStatus status) =>
        _factory.ExecuteDbAsync(async db =>
        {
            var story = new Story
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ConversationId = conversationId,
                Title = title,
                TargetAge = 6,
                ArtStyle = "watercolour",
                Status = status,
            };
            db.Stories.Add(story);
            await db.SaveChangesAsync(Ct);
            return story.Id;
        });
}
