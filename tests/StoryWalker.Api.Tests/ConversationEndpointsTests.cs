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

    // The chat's heading says "New story based on …" (ADR-0029); the history has to say the same
    // thing, rather than whatever short first message the parent typed.
    [Fact]
    public async Task A_related_story_not_yet_created_is_titled_after_the_story_it_builds_on()
    {
        var me = await ResetAndSeedMeAsync();
        var original = await SeedStoryAsync(me, "The Fox and the Moon", StoryStatus.Complete);
        await SeedConversationAsync(
            me,
            ConversationStatus.Active,
            basedOnStoryId: original,
            messages: [(MessageRole.User, "A sequel")]
        );

        Assert.Equal("New story based on “The Fox and the Moon”", Assert.Single(await ListAsync()).Title);
    }

    [Fact]
    public async Task A_related_story_takes_its_own_title_once_created()
    {
        var me = await ResetAndSeedMeAsync();
        var original = await SeedStoryAsync(me, "The Fox and the Moon", StoryStatus.Complete);
        var id = await SeedConversationAsync(
            me,
            ConversationStatus.Completed,
            basedOnStoryId: original,
            messages: [(MessageRole.User, "A sequel")]
        );
        await LinkStoryAsync(id, me, "The Fox in Winter", StoryStatus.Complete);

        var titles = (await ListAsync()).Select(c => c.Title).ToList();

        Assert.Equal(["The Fox in Winter"], titles);
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

    [Fact]
    public async Task Detail_shows_only_what_the_parent_and_the_agent_said()
    {
        var me = await ResetAndSeedMeAsync();
        var id = await SeedConversationAsync(
            me,
            ConversationStatus.Active,
            messages:
            [
                (MessageRole.System, "instructions and saved context"),
                (MessageRole.User, "A story about a fox"),
                (MessageRole.Assistant, "How old is the child?"),
                (MessageRole.System, "GENERATION FAILED. ..."),
                (MessageRole.Assistant, "The picture could not be made. Shall we change the scene?"),
            ]
        );

        var conversation = await _client.GetFromJsonAsync<ConversationDto>($"/stories/conversations/{id}", Ct);

        Assert.NotNull(conversation);
        Assert.Equal(
            [
                new ConversationMessageDto("user", "A story about a fox"),
                new ConversationMessageDto("assistant", "How old is the child?"),
                new ConversationMessageDto("assistant", "The picture could not be made. Shall we change the scene?"),
            ],
            conversation.Messages
        );
        Assert.True(conversation.Resumable);
        Assert.Equal("A story about a fox", conversation.Title);
    }

    // The sentinel is plumbing between the agent and the API; it has no business on screen.
    [Fact]
    public async Task Detail_strips_the_end_of_chat_sentinel()
    {
        var me = await ResetAndSeedMeAsync();
        var id = await SeedConversationAsync(
            me,
            ConversationStatus.Completed,
            messages: [(MessageRole.User, "yes"), (MessageRole.Assistant, $"Generating now. {StoryAgentPrompt.EndOfChatSentinel}")]
        );

        var conversation = await _client.GetFromJsonAsync<ConversationDto>($"/stories/conversations/{id}", Ct);

        Assert.Equal("Generating now.", conversation!.Messages[^1].Text);
        Assert.False(conversation.Resumable);
    }

    [Fact]
    public async Task Detail_of_another_users_conversation_is_404()
    {
        await ResetAndSeedMeAsync();
        var stranger = await _factory.ExecuteDbAsync(db => SeedData.SeedUserAsync(db, "user_conversation_stranger"));
        var theirs = await SeedConversationAsync(stranger, ConversationStatus.Active, messages: [(MessageRole.User, "not yours")]);

        var response = await _client.GetAsync($"/stories/conversations/{theirs}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Detail_of_an_unknown_conversation_is_404()
    {
        await ResetAndSeedMeAsync();

        var response = await _client.GetAsync($"/stories/conversations/{Guid.NewGuid()}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Patch_sets_the_users_own_title_trimmed()
    {
        var me = await ResetAndSeedMeAsync();
        var id = await SeedConversationAsync(me, ConversationStatus.Completed, messages: [(MessageRole.User, "hi")]);
        await LinkStoryAsync(id, me, "The Fox and the Moon", StoryStatus.Complete);

        var response = await _client.PatchAsJsonAsync(
            $"/stories/conversations/{id}",
            new UpdateConversationTitleRequest("  Bedtime ideas  "),
            Ct
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await response.Content.ReadFromJsonAsync<ConversationSummaryDto>(Ct);
        Assert.Equal("Bedtime ideas", summary!.Title);
        // It now beats the story's title in the list too.
        Assert.Equal("Bedtime ideas", Assert.Single(await ListAsync()).Title);
    }

    [Fact]
    public async Task Patch_with_an_empty_title_is_400()
    {
        var me = await ResetAndSeedMeAsync();
        var id = await SeedConversationAsync(me, ConversationStatus.Active, messages: [(MessageRole.User, "hi")]);

        var response = await _client.PatchAsJsonAsync(
            $"/stories/conversations/{id}",
            new UpdateConversationTitleRequest("   "),
            Ct
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_of_another_users_conversation_is_404_and_changes_nothing()
    {
        await ResetAndSeedMeAsync();
        var stranger = await _factory.ExecuteDbAsync(db => SeedData.SeedUserAsync(db, "user_conversation_stranger"));
        var theirs = await SeedConversationAsync(stranger, ConversationStatus.Active, messages: [(MessageRole.User, "not yours")]);

        var response = await _client.PatchAsJsonAsync(
            $"/stories/conversations/{theirs}",
            new UpdateConversationTitleRequest("Mine now"),
            Ct
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(
            await _factory.ExecuteDbAsync(db =>
                db.Conversations.Where(c => c.Id == theirs).Select(c => c.Title).SingleAsync(Ct)
            )
        );
    }

    // Stories and conversations are deleted independently (ADR-0028): the story stays, and only
    // loses its link to the conversation.
    [Fact]
    public async Task Delete_removes_the_conversation_and_its_messages_but_keeps_its_stories()
    {
        var me = await ResetAndSeedMeAsync();
        var id = await SeedConversationAsync(
            me,
            ConversationStatus.Completed,
            messages: [(MessageRole.User, "hi"), (MessageRole.Assistant, "hello")]
        );
        var storyId = await LinkStoryAsync(id, me, "The Fox and the Moon", StoryStatus.Complete);

        var response = await _client.DeleteAsync($"/stories/conversations/{id}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.ExecuteDbAsync(async db =>
        {
            Assert.False(await db.Conversations.AnyAsync(c => c.Id == id, Ct));
            Assert.False(await db.ConversationMessages.AnyAsync(m => m.ConversationId == id, Ct));
            var story = await db.Stories.SingleAsync(s => s.Id == storyId, Ct);
            Assert.Null(story.ConversationId);
        });
    }

    [Fact]
    public async Task Delete_of_another_users_conversation_is_404_and_keeps_it()
    {
        await ResetAndSeedMeAsync();
        var stranger = await _factory.ExecuteDbAsync(db => SeedData.SeedUserAsync(db, "user_conversation_stranger"));
        var theirs = await SeedConversationAsync(stranger, ConversationStatus.Active, messages: [(MessageRole.User, "not yours")]);

        var response = await _client.DeleteAsync($"/stories/conversations/{theirs}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(await _factory.ExecuteDbAsync(db => db.Conversations.AnyAsync(c => c.Id == theirs, Ct)));
    }

    // What "New story based on this one" builds on (ADR-0029): the newest finished story, never a
    // failed attempt that came after it.
    [Fact]
    public async Task Offers_the_most_recent_finished_story_to_build_on()
    {
        var me = await ResetAndSeedMeAsync();
        var id = await SeedConversationAsync(me, ConversationStatus.Completed, messages: [(MessageRole.User, "hi")]);
        await LinkStoryAsync(id, me, "First", StoryStatus.Complete);
        var latestComplete = await LinkStoryAsync(id, me, "Second", StoryStatus.Complete);
        await LinkStoryAsync(id, me, "Third", StoryStatus.Failed);

        Assert.Equal(latestComplete, Assert.Single(await ListAsync()).LatestCompleteStoryId);
        var detail = await _client.GetFromJsonAsync<ConversationDto>($"/stories/conversations/{id}", Ct);
        Assert.Equal(latestComplete, detail!.LatestCompleteStoryId);
    }

    [Fact]
    public async Task Offers_nothing_to_build_on_without_a_finished_story()
    {
        var me = await ResetAndSeedMeAsync();
        var id = await SeedConversationAsync(me, ConversationStatus.Active, messages: [(MessageRole.User, "hi")]);
        await LinkStoryAsync(id, me, "Gone wrong", StoryStatus.Failed);

        Assert.Null(Assert.Single(await ListAsync()).LatestCompleteStoryId);
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
        Guid? basedOnStoryId = null,
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
                BasedOnStoryId = basedOnStoryId,
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

    /// <summary>A story of the user's that no conversation queued, to start a related one from.</summary>
    private Task<Guid> SeedStoryAsync(Guid userId, string title, StoryStatus status) =>
        _factory.ExecuteDbAsync(async db =>
        {
            var story = new Story
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = title,
                TargetAge = 6,
                ArtStyle = "watercolour",
                Status = status,
            };
            db.Stories.Add(story);
            await db.SaveChangesAsync(Ct);
            return story.Id;
        });

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
