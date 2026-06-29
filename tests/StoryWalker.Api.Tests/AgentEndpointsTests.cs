using System.Net;
using System.Net.Http.Json;
using StoryWalker.Application.Api;

namespace StoryWalker.Api.Tests;

/// <summary>
/// End-to-end tests for the agent conversation endpoint, driven over HTTP. The chat backend is a
/// <see cref="FakeStoryAgent"/> (registered by the factory), so these assert the endpoint contract —
/// start vs continue and validation — without contacting Ollama or Foundry.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AgentEndpointsTests(StoryWalkerApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

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
    public async Task ContinueConversation_unknown_id_returns_404()
    {
        var response = await _client.PostAsJsonAsync(
            "/stories/conversations",
            new AgentMessageRequest(Guid.NewGuid().ToString(), Message: "hello?"),
            Ct
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
