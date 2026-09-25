using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using StoryWalker.Application.Api;
using StoryWalker.Domain.Enums;

namespace StoryWalker.Api.Tests;

/// <summary>End-to-end tests for the Story endpoints, driven over HTTP against a real Postgres.</summary>
[Collection(ApiCollection.Name)]
public sealed class StoryEndpointsTests(StoryWalkerApiFactory factory)
{
    private readonly StoryWalkerApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClientAs("user_story_tests");

    // xUnit v3 hands each test a cancellation token (honours timeouts / cancellation); thread it
    // through the async calls so a hung request fails the test promptly instead of hanging.
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Health_returns_healthy()
    {
        var response = await _client.GetAsync("/health", Ct);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains("healthy", body);
    }

    [Fact]
    public async Task GetStories_returns_seeded_summary()
    {
        await _factory.ResetAsync();
        var mineId = await _factory.ExecuteDbAsync(db =>
            SeedData.SeedUserAsync(db, "user_story_tests")
        );
        await _factory.ExecuteDbAsync(db => SeedData.SeedDemoStoryAsync(db, mineId));

        var summaries = await _client.GetFromJsonAsync<List<StorySummaryDto>>("/stories", Ct);

        Assert.NotNull(summaries);
        var summary = Assert.Single(summaries);
        Assert.Equal("Luna and the Rainy Day Adventure", summary.Title);
        Assert.Equal("Complete", summary.Status);
    }

    [Fact]
    public async Task GetStory_returns_full_story_with_pages()
    {
        await _factory.ResetAsync();
        var mineId = await _factory.ExecuteDbAsync(db =>
            SeedData.SeedUserAsync(db, "user_story_tests")
        );
        var storyId = await _factory.ExecuteDbAsync(db => SeedData.SeedDemoStoryAsync(db, mineId));

        var story = await _client.GetFromJsonAsync<StoryDto>($"/stories/{storyId}", Ct);

        Assert.NotNull(story);
        Assert.Equal(storyId, story.Id);
        // Six pages, returned in page-number order.
        Assert.Equal([1, 2, 3, 4, 5, 6], story.Pages.Select(p => p.PageNo).ToArray());
        Assert.Equal(5, story.TargetAge);
    }

    [Fact]
    public async Task GetStory_unknown_id_returns_404()
    {
        await _factory.ResetAsync();

        var response = await _client.GetAsync($"/stories/{Guid.NewGuid()}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PatchStory_updates_title_and_trims_whitespace()
    {
        await _factory.ResetAsync();
        var mineId = await _factory.ExecuteDbAsync(db =>
            SeedData.SeedUserAsync(db, "user_story_tests")
        );
        var storyId = await _factory.ExecuteDbAsync(db => SeedData.SeedDemoStoryAsync(db, mineId));

        var response = await _client.PatchAsJsonAsync(
            $"/stories/{storyId}",
            new UpdateStoryTitleRequest("  Renamed Story  "),
            Ct
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<StoryDto>(Ct);
        Assert.NotNull(updated);
        Assert.Equal("Renamed Story", updated.Title);

        // Persisted, not just echoed back.
        var persistedTitle = await _factory.ExecuteDbAsync(db =>
            db.Stories.Where(s => s.Id == storyId).Select(s => s.Title).FirstAsync(Ct)
        );
        Assert.Equal("Renamed Story", persistedTitle);
    }

    // A failed story can only be deleted (ADR-0026). The dashboard hides the rename action; this
    // is the server holding the same line.
    [Fact]
    public async Task PatchStory_of_a_failed_story_returns_409_and_does_not_rename()
    {
        await _factory.ResetAsync();
        var mineId = await _factory.ExecuteDbAsync(db =>
            SeedData.SeedUserAsync(db, "user_story_tests")
        );
        var storyId = await _factory.ExecuteDbAsync(async db =>
        {
            var id = await SeedData.SeedDemoStoryAsync(db, mineId);
            var story = await db.Stories.SingleAsync(s => s.Id == id, Ct);
            story.Status = StoryStatus.Failed;
            await db.SaveChangesAsync(Ct);
            return id;
        });

        var response = await _client.PatchAsJsonAsync(
            $"/stories/{storyId}",
            new UpdateStoryTitleRequest("Renamed Story"),
            Ct
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var persistedTitle = await _factory.ExecuteDbAsync(db =>
            db.Stories.Where(s => s.Id == storyId).Select(s => s.Title).FirstAsync(Ct)
        );
        Assert.Equal("Luna and the Rainy Day Adventure", persistedTitle);
    }

    [Fact]
    public async Task PatchStory_empty_title_returns_400()
    {
        await _factory.ResetAsync();
        var mineId = await _factory.ExecuteDbAsync(db =>
            SeedData.SeedUserAsync(db, "user_story_tests")
        );
        var storyId = await _factory.ExecuteDbAsync(db => SeedData.SeedDemoStoryAsync(db, mineId));

        var response = await _client.PatchAsJsonAsync(
            $"/stories/{storyId}",
            new UpdateStoryTitleRequest("   "),
            Ct
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchStory_unknown_id_returns_404()
    {
        await _factory.ResetAsync();

        var response = await _client.PatchAsJsonAsync(
            $"/stories/{Guid.NewGuid()}",
            new UpdateStoryTitleRequest("New title"),
            Ct
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteStory_removes_story_and_cascades_pages_but_keeps_the_user()
    {
        await _factory.ResetAsync();
        var mineId = await _factory.ExecuteDbAsync(db =>
            SeedData.SeedUserAsync(db, "user_story_tests")
        );
        var storyId = await _factory.ExecuteDbAsync(db => SeedData.SeedDemoStoryAsync(db, mineId));

        var response = await _client.DeleteAsync($"/stories/{storyId}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.ExecuteDbAsync(async db =>
        {
            Assert.Equal(0, await db.Stories.CountAsync(Ct));
            Assert.Equal(0, await db.Pages.CountAsync(Ct));
            // The owning user survives the story deletion.
            Assert.Equal(1, await db.Users.CountAsync(Ct));
        });
    }

    // Deletes stay independent (ADR-0028, ADR-0029): a sequel outlives the story it was based on.
    [Fact]
    public async Task DeleteStory_keeps_the_stories_based_on_it_unlinked()
    {
        await _factory.ResetAsync();
        var mineId = await _factory.ExecuteDbAsync(db => SeedData.SeedUserAsync(db, "user_story_tests"));
        var original = await _factory.ExecuteDbAsync(db => SeedData.SeedDemoStoryAsync(db, mineId));
        var sequel = await _factory.ExecuteDbAsync(async db =>
        {
            var id = await SeedData.SeedDemoStoryAsync(db, mineId);
            (await db.Stories.SingleAsync(s => s.Id == id, Ct)).BasedOnStoryId = original;
            await db.SaveChangesAsync(Ct);
            return id;
        });

        var response = await _client.DeleteAsync($"/stories/{original}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var basedOn = await _factory.ExecuteDbAsync(db =>
            db.Stories.Where(s => s.Id == sequel).Select(s => s.BasedOnStoryId).SingleAsync(Ct)
        );
        Assert.Null(basedOn);
    }

    [Fact]
    public async Task DeleteStory_unknown_id_returns_404()
    {
        await _factory.ResetAsync();

        var response = await _client.DeleteAsync($"/stories/{Guid.NewGuid()}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetStories_excludes_other_users_stories()
    {
        await _factory.ResetAsync();
        var strangerId = await _factory.ExecuteDbAsync(db =>
            SeedData.SeedUserAsync(db, "user_stranger")
        );
        await _factory.ExecuteDbAsync(db => SeedData.SeedDemoStoryAsync(db, strangerId));

        var summaries = await _client.GetFromJsonAsync<List<StorySummaryDto>>("/stories", Ct);

        Assert.NotNull(summaries);
        Assert.Empty(summaries);
    }

    [Fact]
    public async Task GetStory_of_another_user_returns_404()
    {
        await _factory.ResetAsync();
        var strangerId = await _factory.ExecuteDbAsync(db =>
            SeedData.SeedUserAsync(db, "user_stranger")
        );
        var storyId = await _factory.ExecuteDbAsync(db =>
            SeedData.SeedDemoStoryAsync(db, strangerId)
        );

        var response = await _client.GetAsync($"/stories/{storyId}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PatchStory_of_another_user_returns_404_and_does_not_rename()
    {
        await _factory.ResetAsync();
        var strangerId = await _factory.ExecuteDbAsync(db =>
            SeedData.SeedUserAsync(db, "user_stranger")
        );
        var storyId = await _factory.ExecuteDbAsync(db =>
            SeedData.SeedDemoStoryAsync(db, strangerId)
        );

        var response = await _client.PatchAsJsonAsync(
            $"/stories/{storyId}",
            new UpdateStoryTitleRequest("Hijacked"),
            Ct
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var title = await _factory.ExecuteDbAsync(db =>
            db.Stories.Where(s => s.Id == storyId).Select(s => s.Title).SingleAsync(Ct)
        );
        Assert.NotEqual("Hijacked", title);
    }

    [Fact]
    public async Task DeleteStory_of_another_user_returns_404_and_keeps_the_story()
    {
        await _factory.ResetAsync();
        var strangerId = await _factory.ExecuteDbAsync(db =>
            SeedData.SeedUserAsync(db, "user_stranger")
        );
        var storyId = await _factory.ExecuteDbAsync(db =>
            SeedData.SeedDemoStoryAsync(db, strangerId)
        );

        var response = await _client.DeleteAsync($"/stories/{storyId}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(
            await _factory.ExecuteDbAsync(db => db.Stories.AnyAsync(s => s.Id == storyId, Ct))
        );
    }
}
