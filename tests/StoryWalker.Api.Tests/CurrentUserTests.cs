using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using StoryWalker.Application.Api;

namespace StoryWalker.Api.Tests;

/// <summary>
/// Covers just-in-time provisioning through real requests, since a scoped service is only ever
/// reached that way. The conversations endpoint is the driver: at this point it is the only one
/// that resolves the caller (the story reads are scoped in the next task).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CurrentUserTests(StoryWalkerApiFactory factory)
{
    private readonly StoryWalkerApiFactory _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AgentMessageRequest Turn(string message) => new(ConversationId: null, message);

    [Fact]
    public async Task First_authenticated_request_creates_the_user()
    {
        await _factory.ResetAsync();
        var client = _factory.CreateClientAs("user_new", "new@storywalker.app");

        await client.PostAsJsonAsync("/stories/conversations", Turn("ciao"), Ct);

        var user = await _factory.ExecuteDbAsync(db =>
            db.Users.SingleAsync(u => u.ClerkUserId == "user_new", Ct)
        );
        Assert.Equal("new@storywalker.app", user.Email);
    }

    [Fact]
    public async Task Second_request_reuses_the_same_user()
    {
        await _factory.ResetAsync();
        var client = _factory.CreateClientAs("user_repeat");

        await client.PostAsJsonAsync("/stories/conversations", Turn("ciao"), Ct);
        await client.PostAsJsonAsync("/stories/conversations", Turn("ancora"), Ct);

        var count = await _factory.ExecuteDbAsync(db =>
            db.Users.CountAsync(u => u.ClerkUserId == "user_repeat", Ct)
        );
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Concurrent_first_requests_create_one_user()
    {
        await _factory.ResetAsync();

        // Separate clients so the two requests cannot share a scope. The unique index on
        // ClerkUserId decides the race; the loser must re-read rather than surface a 500.
        var responses = await Task.WhenAll(
            _factory
                .CreateClientAs("user_race")
                .PostAsJsonAsync("/stories/conversations", Turn("ciao"), Ct),
            _factory
                .CreateClientAs("user_race")
                .PostAsJsonAsync("/stories/conversations", Turn("ciao"), Ct)
        );

        Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode));
        var count = await _factory.ExecuteDbAsync(db =>
            db.Users.CountAsync(u => u.ClerkUserId == "user_race", Ct)
        );
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task New_story_is_owned_by_the_authenticated_user()
    {
        await _factory.ResetAsync();
        var client = _factory.CreateClientAs("user_owner");

        var response = await client.PostAsJsonAsync(
            "/stories/conversations",
            Turn(FakeStoryAgent.ConfirmMessage),
            Ct
        );

        response.EnsureSuccessStatusCode();
        var owner = await _factory.ExecuteDbAsync(db =>
            db.Stories.Select(s => s.User.ClerkUserId).SingleAsync(Ct)
        );
        Assert.Equal("user_owner", owner);
    }
}
