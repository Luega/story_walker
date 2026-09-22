using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using StoryWalker.Application.Api;

namespace StoryWalker.Api.Tests;

/// <summary>
/// End-to-end tests for <c>/me/settings</c>, driven over HTTP against the real database. The
/// settings are the user's standing context for the story agent, so these pin the contract the
/// settings page relies on: what a brand-new account reads, what a save actually stores, and that
/// one account can never read or write another's.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class UserSettingsEndpointsTests(StoryWalkerApiFactory factory)
{
    private readonly StoryWalkerApiFactory _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_user_has_no_saved_context()
    {
        await _factory.ResetAsync();
        var client = _factory.CreateClientAs("user_settings_new");

        var response = await client.GetAsync("/me/settings", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserSettingsDto>(Ct);
        Assert.NotNull(body);
        Assert.Null(body.Kids);
        Assert.Null(body.Story);
        Assert.Null(body.Characters);
        Assert.Null(body.ArtStyle);
    }

    [Fact]
    public async Task Saving_settings_round_trips_through_the_database()
    {
        await _factory.ResetAsync();
        var client = _factory.CreateClientAs("user_settings_save");

        var saved = await client.PatchAsJsonAsync(
            "/me/settings",
            new UserSettingsDto(
                Kids: "Mia is 5 and loves dinosaurs",
                Story: "Short, gentle, always a kind ending",
                Characters: "Pip, a small grey kitten with green eyes",
                ArtStyle: "soft watercolour"
            ),
            Ct
        );

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var reread = await client.GetFromJsonAsync<UserSettingsDto>("/me/settings", Ct);
        Assert.NotNull(reread);
        Assert.Equal("Mia is 5 and loves dinosaurs", reread.Kids);
        Assert.Equal("Short, gentle, always a kind ending", reread.Story);
        Assert.Equal("Pip, a small grey kitten with green eyes", reread.Characters);
        Assert.Equal("soft watercolour", reread.ArtStyle);
    }

    /// <summary>The response body is what was stored, so the form can render the truth after a save.</summary>
    [Fact]
    public async Task The_patch_response_carries_the_stored_values()
    {
        await _factory.ResetAsync();
        var client = _factory.CreateClientAs("user_settings_echo");

        var response = await client.PatchAsJsonAsync(
            "/me/settings",
            new UserSettingsDto("  Mia is 5  ", null, null, null),
            Ct
        );

        var body = await response.Content.ReadFromJsonAsync<UserSettingsDto>(Ct);
        Assert.NotNull(body);
        Assert.Equal("Mia is 5", body.Kids);
    }

    [Fact]
    public async Task Answers_are_trimmed_and_blank_ones_are_stored_as_null()
    {
        await _factory.ResetAsync();
        var client = _factory.CreateClientAs("user_settings_trim");

        await client.PatchAsJsonAsync(
            "/me/settings",
            new UserSettingsDto(Kids: "\n  Mia is 5  ", Story: "   ", Characters: "", ArtStyle: null),
            Ct
        );

        var stored = await _factory.ExecuteDbAsync(db =>
            db
                .Users.Where(u => u.ClerkUserId == "user_settings_trim")
                .Select(u => new
                {
                    u.KidsContext,
                    u.StoryContext,
                    u.CharactersContext,
                    u.ArtStyleContext,
                })
                .SingleAsync(Ct)
        );

        Assert.Equal("Mia is 5", stored.KidsContext);
        Assert.Null(stored.StoryContext);
        Assert.Null(stored.CharactersContext);
        Assert.Null(stored.ArtStyleContext);
    }

    /// <summary>The form submits all four fields, so a save replaces the set rather than merging into it.</summary>
    [Fact]
    public async Task Saving_replaces_the_whole_set_so_a_cleared_answer_is_cleared()
    {
        await _factory.ResetAsync();
        var client = _factory.CreateClientAs("user_settings_clear");

        await client.PatchAsJsonAsync(
            "/me/settings",
            new UserSettingsDto("Mia is 5", "Gentle", "Pip the kitten", "watercolour"),
            Ct
        );

        await client.PatchAsJsonAsync(
            "/me/settings",
            new UserSettingsDto("Mia is 5", null, null, null),
            Ct
        );

        var reread = await client.GetFromJsonAsync<UserSettingsDto>("/me/settings", Ct);
        Assert.NotNull(reread);
        Assert.Equal("Mia is 5", reread.Kids);
        Assert.Null(reread.Story);
        Assert.Null(reread.Characters);
        Assert.Null(reread.ArtStyle);
    }

    [Fact]
    public async Task An_answer_over_the_cap_is_rejected_and_names_the_field()
    {
        await _factory.ResetAsync();
        var client = _factory.CreateClientAs("user_settings_toolong");

        var response = await client.PatchAsJsonAsync(
            "/me/settings",
            new UserSettingsDto(
                Kids: null,
                Story: null,
                Characters: null,
                ArtStyle: new string('x', UserSettingsDto.MaxFieldLength + 1)
            ),
            Ct
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemBody>(Ct);
        Assert.NotNull(problem);
        Assert.True(problem.Errors.ContainsKey("artStyle"));
    }

    /// <summary>Exactly at the cap is a valid answer; the boundary belongs to the user, not the guard.</summary>
    [Fact]
    public async Task An_answer_exactly_at_the_cap_is_accepted()
    {
        await _factory.ResetAsync();
        var client = _factory.CreateClientAs("user_settings_atcap");

        var response = await client.PatchAsJsonAsync(
            "/me/settings",
            new UserSettingsDto(
                Kids: new string('x', UserSettingsDto.MaxFieldLength),
                null,
                null,
                null
            ),
            Ct
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Length is judged after trimming, so trailing whitespace cannot fail a valid answer.</summary>
    [Fact]
    public async Task Length_is_measured_after_trimming()
    {
        await _factory.ResetAsync();
        var client = _factory.CreateClientAs("user_settings_trimcap");

        var response = await client.PatchAsJsonAsync(
            "/me/settings",
            new UserSettingsDto(
                Kids: new string('x', UserSettingsDto.MaxFieldLength) + "     ",
                null,
                null,
                null
            ),
            Ct
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task One_users_settings_are_invisible_to_another()
    {
        await _factory.ResetAsync();
        var alice = _factory.CreateClientAs("user_settings_alice");
        var bob = _factory.CreateClientAs("user_settings_bob");

        await alice.PatchAsJsonAsync(
            "/me/settings",
            new UserSettingsDto("Mia is 5", null, null, null),
            Ct
        );

        var bobsSettings = await bob.GetFromJsonAsync<UserSettingsDto>("/me/settings", Ct);
        Assert.NotNull(bobsSettings);
        Assert.Null(bobsSettings.Kids);

        // And Bob writing his own must not disturb Alice's.
        await bob.PatchAsJsonAsync(
            "/me/settings",
            new UserSettingsDto("Leo is 7", null, null, null),
            Ct
        );

        var alicesSettings = await alice.GetFromJsonAsync<UserSettingsDto>("/me/settings", Ct);
        Assert.NotNull(alicesSettings);
        Assert.Equal("Mia is 5", alicesSettings.Kids);
    }

    [Fact]
    public async Task Settings_require_authentication()
    {
        await _factory.ResetAsync();
        var anonymous = _factory.CreateClient();

        var get = await anonymous.GetAsync("/me/settings", Ct);
        var patch = await anonymous.PatchAsJsonAsync(
            "/me/settings",
            new UserSettingsDto(null, null, null, null),
            Ct
        );

        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, patch.StatusCode);
    }

    /// <summary>Just enough of ProblemDetails to assert which field the API blamed.</summary>
    private sealed record ValidationProblemBody(Dictionary<string, string[]> Errors);
}
