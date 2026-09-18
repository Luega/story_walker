using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using StoryWalker.Application.Api;
using StoryWalker.Domain.Enums;

namespace StoryWalker.Api.Tests;

/// <summary>
/// End-to-end tests for the agent conversation endpoint, driven over HTTP. The chat backend is a
/// <see cref="FakeStoryAgent"/> (registered by the factory), so these assert the endpoint contract —
/// start vs continue and validation — without contacting Foundry.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AgentEndpointsTests(StoryWalkerApiFactory factory)
{
    private readonly StoryWalkerApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClientAs("user_agent_tests");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StartConversation_returns_reply_and_new_conversation_id()
    {
        var response = await _client.PostAsJsonAsync(
            "/stories/conversations",
            new AgentMessageRequest(ConversationId: null, Message: "I want a story about a fox"),
            Ct
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AgentMessageResponse>(Ct);
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body.ConversationId));
        Assert.Equal("Echo: I want a story about a fox", body.Reply);
        Assert.False(body.BriefReady);
    }

    [Fact]
    public async Task ContinueConversation_echoes_the_same_conversation_id()
    {
        var start = await _client.PostAsJsonAsync(
            "/stories/conversations",
            new AgentMessageRequest(ConversationId: null, Message: "first"),
            Ct
        );
        var started = await start.Content.ReadFromJsonAsync<AgentMessageResponse>(Ct);
        Assert.NotNull(started);

        var response = await _client.PostAsJsonAsync(
            "/stories/conversations",
            new AgentMessageRequest(started.ConversationId, Message: "second"),
            Ct
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AgentMessageResponse>(Ct);
        Assert.NotNull(body);
        Assert.Equal(started.ConversationId, body.ConversationId);
    }

    [Fact]
    public async Task SendMessage_blank_message_returns_400()
    {
        var response = await _client.PostAsJsonAsync(
            "/stories/conversations",
            new AgentMessageRequest(ConversationId: null, Message: "   "),
            Ct
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BriefReady_persists_generating_story_publishes_brief_and_returns_202()
    {
        await _factory.ResetAsync();

        var response = await _client.PostAsJsonAsync(
            "/stories/conversations",
            new AgentMessageRequest(ConversationId: null, Message: FakeStoryAgent.ConfirmMessage),
            Ct
        );

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var queued = await response.Content.ReadFromJsonAsync<StoryQueuedResponse>(Ct);
        Assert.NotNull(queued);
        Assert.Equal("Generating", queued.Status);
        Assert.Equal($"/stories/{queued.StoryId}", response.Headers.Location?.ToString());

        // The story row exists in Generating with the brief's fields.
        var story = await _factory.ExecuteDbAsync(db =>
            db.Stories.SingleAsync(s => s.Id == queued.StoryId, Ct)
        );
        Assert.Equal(StoryStatus.Generating, story.Status);
        Assert.Equal(FakeStoryAgent.SampleBrief.Title, story.Title);
        Assert.Equal(FakeStoryAgent.SampleBrief.ArtStyle, story.ArtStyle);
        Assert.Equal(FakeStoryAgent.SampleBrief.TargetAge, story.TargetAge);

        // The brief was enqueued for that same story id.
        Assert.Contains(
            _factory.Publisher.Published,
            p => p.StoryId == queued.StoryId && p.Brief.Title == FakeStoryAgent.SampleBrief.Title
        );
    }

    [Fact]
    public async Task ContinueConversation_unknown_id_returns_404()
    {
        var response = await _client.PostAsJsonAsync(
            "/stories/conversations",
            new AgentMessageRequest(Guid.NewGuid().ToString(), Message: "hello?"),
            Ct
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Continuing_another_users_conversation_returns_404()
    {
        await _factory.ResetAsync();

        // The owner starts a conversation and gets its id back.
        var started = await _client.PostAsJsonAsync(
            "/stories/conversations",
            new AgentMessageRequest(ConversationId: null, Message: "ciao"),
            Ct
        );
        var owned = await started.Content.ReadFromJsonAsync<AgentMessageResponse>(Ct);
        Assert.NotNull(owned);

        // A different signed-in user presents that id.
        var stranger = _factory.CreateClientAs("user_conversation_thief");
        var response = await stranger.PostAsJsonAsync(
            "/stories/conversations",
            new AgentMessageRequest(owned.ConversationId, "continua"),
            Ct
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
