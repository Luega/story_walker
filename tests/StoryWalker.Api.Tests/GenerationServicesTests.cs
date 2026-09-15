using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using StoryWalker.Application.Brief;
using StoryWalker.Domain.Entities;
using StoryWalker.Domain.Enums;
using StoryWalker.Infrastructure.Generation;
using StoryWalker.Infrastructure.Persistence;

namespace StoryWalker.Api.Tests;

/// <summary>
/// Tests the services the Durable activities delegate to (ADR-0004). The activities themselves are
/// two-line shells, so testing these against a real database covers the logic without a Durable
/// test harness. Idempotency is the contract ADR-0005 obliges us to prove.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class GenerationServicesTests(StoryWalkerApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly StoryWalkerApiFactory _factory = factory;

    private static GenerationBrief Brief() =>
        new(
            Title: "The Fox and the Moon",
            TargetAge: 6,
            ArtStyle: "soft watercolour",
            Pages:
            [
                .. Enumerable
                    .Range(1, GenerationBriefValidator.PageCount)
                    .Select(n => new BriefPage(n, $"Page {n} text.", $"Fox, a small red fox, scene {n}")),
            ]
        );

    /// <summary>Inserts a user + a Generating story, the state the API leaves behind on enqueue.</summary>
    private async Task<Guid> SeedGeneratingStoryAsync()
    {
        var storyId = Guid.NewGuid();
        await _factory.ExecuteDbAsync(async db =>
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                ClerkUserId = $"user_{Guid.NewGuid():N}",
                Email = "worker@storywalker.test",
            };
            db.Users.Add(user);
            db.Stories.Add(
                new Story
                {
                    Id = storyId,
                    UserId = user.Id,
                    Title = "The Fox and the Moon",
                    TargetAge = 6,
                    ArtStyle = "soft watercolour",
                    Status = StoryStatus.Generating,
                }
            );
            await db.SaveChangesAsync(Ct);
        });
        return storyId;
    }

    [Fact]
    public async Task PersistPages_writes_one_page_per_brief_page()
    {
        await _factory.ResetAsync();
        var storyId = await SeedGeneratingStoryAsync();

        await _factory.ExecuteDbAsync(db => new StoryPersister(db).PersistPagesAsync(storyId, Brief(), Ct));

        await _factory.ExecuteDbAsync(async db =>
        {
            var pages = await db.Pages.Where(p => p.StoryId == storyId).OrderBy(p => p.PageNo).ToListAsync(Ct);
            Assert.Equal([1, 2, 3, 4, 5, 6], pages.Select(p => p.PageNo));
            Assert.All(pages, p => Assert.Equal(PageStatus.Pending, p.Status));
            Assert.All(pages, p => Assert.Null(p.ImageUrl));
            Assert.Equal("Page 1 text.", pages[0].Content);
        });
    }

    [Fact]
    public async Task PersistPages_run_twice_does_not_duplicate_pages()
    {
        await _factory.ResetAsync();
        var storyId = await SeedGeneratingStoryAsync();
        var brief = Brief();

        // A redelivered Service Bus message replays the activity (ADR-0005).
        await _factory.ExecuteDbAsync(db => new StoryPersister(db).PersistPagesAsync(storyId, brief, Ct));
        await _factory.ExecuteDbAsync(db => new StoryPersister(db).PersistPagesAsync(storyId, brief, Ct));

        var count = await _factory.ExecuteDbAsync(db => db.Pages.CountAsync(p => p.StoryId == storyId, Ct));
        Assert.Equal(GenerationBriefValidator.PageCount, count);
    }

    [Fact]
    public async Task Render_sets_the_image_url_and_completes_the_page()
    {
        await _factory.ResetAsync();
        var storyId = await SeedGeneratingStoryAsync();
        var generator = new FakeImageGenerator();
        var store = new InMemoryImageStore();

        await _factory.ExecuteDbAsync(async db =>
        {
            await new StoryPersister(db).PersistPagesAsync(storyId, Brief(), Ct);
            Assert.True(await NewRenderer(db, generator, store).RenderAsync(storyId, 1, Ct));
        });

        await _factory.ExecuteDbAsync(async db =>
        {
            var page = await db.Pages.SingleAsync(p => p.StoryId == storyId && p.PageNo == 1, Ct);
            Assert.Equal(PageStatus.Complete, page.Status);
            Assert.NotNull(page.ImageUrl);
            Assert.Contains(page.ImageUrl, store.Saved.Keys);
        });

        // The prompt carries the page's own description plus the story-wide style suffix that is
        // identical on every page — the whole consistency mechanism (ADR-0019).
        Assert.Single(generator.Prompts);
        Assert.Contains("Fox, a small red fox, scene 1", generator.Prompts[0]);
        Assert.Contains("Art style: soft watercolour", generator.Prompts[0]);
        Assert.Contains("6-year-old", generator.Prompts[0]);
    }

    [Fact]
    public async Task Render_run_twice_calls_the_model_once()
    {
        await _factory.ResetAsync();
        var storyId = await SeedGeneratingStoryAsync();
        var generator = new FakeImageGenerator();
        var store = new InMemoryImageStore();

        await _factory.ExecuteDbAsync(async db =>
        {
            await new StoryPersister(db).PersistPagesAsync(storyId, Brief(), Ct);
            await NewRenderer(db, generator, store).RenderAsync(storyId, 1, Ct);
        });

        // A Durable retry replays the activity; the page already has an image, so no second call.
        await _factory.ExecuteDbAsync(db => NewRenderer(db, generator, store).RenderAsync(storyId, 1, Ct));

        Assert.Equal(1, generator.CallCount);
    }

    [Fact]
    public async Task Content_filter_rejection_fails_only_that_page()
    {
        await _factory.ResetAsync();
        var storyId = await SeedGeneratingStoryAsync();
        var generator = new FakeImageGenerator
        {
            ThrowOnGenerate = new InvalidOperationException(
                "The response was filtered due to the prompt triggering ResponsibleAIPolicyViolation."
            ),
        };

        await _factory.ExecuteDbAsync(async db =>
        {
            await new StoryPersister(db).PersistPagesAsync(storyId, Brief(), Ct);
            Assert.False(await NewRenderer(db, generator, new InMemoryImageStore()).RenderAsync(storyId, 1, Ct));
        });

        await _factory.ExecuteDbAsync(async db =>
        {
            var page = await db.Pages.SingleAsync(p => p.StoryId == storyId && p.PageNo == 1, Ct);
            Assert.Equal(PageStatus.Failed, page.Status);
            Assert.Null(page.ImageUrl);
        });
    }

    [Fact]
    public async Task Transient_failure_is_retried_then_rethrown_for_redelivery()
    {
        await _factory.ResetAsync();
        var storyId = await SeedGeneratingStoryAsync();
        var generator = new FakeImageGenerator
        {
            ThrowOnGenerate = new HttpRequestException("throttled", null, HttpStatusCode.TooManyRequests),
        };

        await _factory.ExecuteDbAsync(async db =>
        {
            await new StoryPersister(db).PersistPagesAsync(storyId, Brief(), Ct);
            await Assert.ThrowsAsync<HttpRequestException>(
                () => NewRenderer(db, generator, new InMemoryImageStore()).RenderAsync(storyId, 1, Ct)
            );
        });

        // Backed off and retried in-process rather than throwing on the first 429 (ADR-0020):
        // an immediate throw abandons the message, which Service Bus makes available again at once.
        Assert.Equal(3, generator.CallCount);
    }

    [Fact]
    public async Task Transient_failure_that_clears_on_retry_succeeds()
    {
        await _factory.ResetAsync();
        var storyId = await SeedGeneratingStoryAsync();
        var generator = new FakeImageGenerator
        {
            ThrowOnGenerate = new HttpRequestException("throttled", null, HttpStatusCode.TooManyRequests),
            FailuresBeforeSuccess = 2,
        };

        await _factory.ExecuteDbAsync(async db =>
        {
            await new StoryPersister(db).PersistPagesAsync(storyId, Brief(), Ct);
            Assert.True(await NewRenderer(db, generator, new InMemoryImageStore()).RenderAsync(storyId, 1, Ct));
        });

        Assert.Equal(3, generator.CallCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task Permanent_http_failure_fails_the_page_without_retrying(HttpStatusCode status)
    {
        await _factory.ResetAsync();
        var storyId = await SeedGeneratingStoryAsync();
        var generator = new FakeImageGenerator
        {
            ThrowOnGenerate = new HttpRequestException("permanent", null, status),
        };

        await _factory.ExecuteDbAsync(async db =>
        {
            await new StoryPersister(db).PersistPagesAsync(storyId, Brief(), Ct);
            // No throw: throwing would abandon the message and Service Bus would redeliver it ten
            // times for an error that cannot change — the exact loop that dead-lettered a real
            // story when the image route returned 404 (2026-09-15).
            Assert.False(await NewRenderer(db, generator, new InMemoryImageStore()).RenderAsync(storyId, 1, Ct));
        });

        Assert.Equal(1, generator.CallCount);
        var page = await _factory.ExecuteDbAsync(db =>
            db.Pages.SingleAsync(p => p.StoryId == storyId && p.PageNo == 1, Ct)
        );
        Assert.Equal(PageStatus.Failed, page.Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task Server_side_failure_is_retried(HttpStatusCode status)
    {
        await _factory.ResetAsync();
        var storyId = await SeedGeneratingStoryAsync();
        var generator = new FakeImageGenerator
        {
            ThrowOnGenerate = new HttpRequestException("server", null, status),
            FailuresBeforeSuccess = 1,
        };

        await _factory.ExecuteDbAsync(async db =>
        {
            await new StoryPersister(db).PersistPagesAsync(storyId, Brief(), Ct);
            Assert.True(await NewRenderer(db, generator, new InMemoryImageStore()).RenderAsync(storyId, 1, Ct));
        });

        Assert.Equal(2, generator.CallCount);
    }

    [Fact]
    public async Task Network_failure_without_a_status_is_retried()
    {
        await _factory.ResetAsync();
        var storyId = await SeedGeneratingStoryAsync();
        var generator = new FakeImageGenerator
        {
            // No StatusCode: the request never got an HTTP answer (DNS, reset connection).
            ThrowOnGenerate = new HttpRequestException("connection reset"),
            FailuresBeforeSuccess = 1,
        };

        await _factory.ExecuteDbAsync(async db =>
        {
            await new StoryPersister(db).PersistPagesAsync(storyId, Brief(), Ct);
            Assert.True(await NewRenderer(db, generator, new InMemoryImageStore()).RenderAsync(storyId, 1, Ct));
        });

        Assert.Equal(2, generator.CallCount);
    }

    [Fact]
    public async Task Content_filter_rejection_is_not_retried()
    {
        await _factory.ResetAsync();
        var storyId = await SeedGeneratingStoryAsync();
        var generator = new FakeImageGenerator
        {
            ThrowOnGenerate = new InvalidOperationException("ResponsibleAIPolicyViolation"),
        };

        await _factory.ExecuteDbAsync(async db =>
        {
            await new StoryPersister(db).PersistPagesAsync(storyId, Brief(), Ct);
            await NewRenderer(db, generator, new InMemoryImageStore()).RenderAsync(storyId, 1, Ct);
        });

        // Retrying a prompt the filter rejects wastes the budget on a certain refusal.
        Assert.Equal(1, generator.CallCount);
    }

    [Fact]
    public async Task CompleteStory_marks_complete_when_at_least_one_page_rendered()
    {
        await _factory.ResetAsync();
        var storyId = await SeedGeneratingStoryAsync();
        var generator = new FakeImageGenerator();

        await _factory.ExecuteDbAsync(async db =>
        {
            await new StoryPersister(db).PersistPagesAsync(storyId, Brief(), Ct);
            await NewRenderer(db, generator, new InMemoryImageStore()).RenderAsync(storyId, 1, Ct);
            await new StoryPersister(db).CompleteStoryAsync(storyId, Ct);
        });

        var status = await _factory.ExecuteDbAsync(db =>
            db.Stories.Where(s => s.Id == storyId).Select(s => s.Status).SingleAsync(Ct)
        );
        Assert.Equal(StoryStatus.Complete, status);
    }

    [Fact]
    public async Task CompleteStory_marks_failed_when_no_page_rendered()
    {
        await _factory.ResetAsync();
        var storyId = await SeedGeneratingStoryAsync();

        await _factory.ExecuteDbAsync(async db =>
        {
            await new StoryPersister(db).PersistPagesAsync(storyId, Brief(), Ct);
            await new StoryPersister(db).CompleteStoryAsync(storyId, Ct);
        });

        var status = await _factory.ExecuteDbAsync(db =>
            db.Stories.Where(s => s.Id == storyId).Select(s => s.Status).SingleAsync(Ct)
        );
        Assert.Equal(StoryStatus.Failed, status);
    }

    private static PageImageRenderer NewRenderer(
        StoryWalkerDbContext db,
        IImageGenerator generator,
        InMemoryImageStore store
    ) =>
        new(db, generator, store, new StubHttpClientFactory(), NullLogger<PageImageRenderer>.Instance)
        {
            // No real waiting in tests; the backoff itself is asserted via the call count.
            FirstRetryDelay = TimeSpan.Zero,
        };

    /// <summary>The URL fallback is never taken by the fake generator, which returns bytes.</summary>
    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
