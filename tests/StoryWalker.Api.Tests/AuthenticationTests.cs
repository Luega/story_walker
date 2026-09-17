using System.Net;
using System.Net.Http.Headers;

namespace StoryWalker.Api.Tests;

/// <summary>
/// Covers the JWT bearer middleware itself: the production configuration runs in these tests, with
/// only the signing key swapped for an in-memory one, so a broken issuer or azp rule fails here.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AuthenticationTests(StoryWalkerApiFactory factory)
{
    private readonly StoryWalkerApiFactory _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Health_is_anonymous()
    {
        var response = await _factory.CreateClient().GetAsync("/health", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Stories_without_a_token_returns_401()
    {
        var response = await _factory.CreateClient().GetAsync("/stories", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Stories_with_a_token_from_an_unauthorized_party_returns_401()
    {
        var client = _factory.CreateClient();
        var token = StoryWalkerApiFactory.CreateToken(
            "user_evil",
            "evil@storywalker.app",
            authorizedParty: "https://evil.example"
        );
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/stories", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Stories_with_a_valid_token_returns_200()
    {
        await _factory.ResetAsync();

        var response = await _factory.CreateClientAs("user_alice").GetAsync("/stories", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
