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
            Assert.Equal(PageRenderOutcome.Rendered, await NewRenderer(db, generator, store).RenderAsync(storyId, 1, Ct));
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
            Assert.Equal(PageRenderOutcome.Refused, await NewRenderer(db, generator, new InMemoryImageStore()).RenderAsync(storyId, 1, Ct));
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
            Assert.Equal(PageRenderOutcome.Rendered, await NewRenderer(db, generator, new InMemoryImageStore()).RenderAsync(storyId, 1, Ct));
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
        var chat = new RecordingChatClient { Reply = RewrittenPrompt };

        await _factory.ExecuteDbAsync(async db =>
        {
            await new StoryPersister(db).PersistPagesAsync(storyId, Brief(), Ct);
            // No throw: throwing would abandon the message and Service Bus would redeliver it ten
            // times for an error that cannot change — the exact loop that dead-lettered a real
            // story when the image route returned 404 (2026-09-15).
            Assert.Equal(PageRenderOutcome.Failed, await NewRenderer(db, generator, new InMemoryImageStore(), chat).RenderAsync(storyId, 1, Ct));
        });

        Assert.Equal(1, generator.CallCount);
        // Not a moderation refusal, so rewording the prompt would change nothing (ADR-0025).
        Assert.Empty(chat.Requests);
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
            Assert.Equal(PageRenderOutcome.Rendered, await NewRenderer(db, generator, new InMemoryImageStore()).RenderAsync(storyId, 1, Ct));
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
            Assert.Equal(PageRenderOutcome.Rendered, await NewRenderer(db, generator, new InMemoryImageStore()).RenderAsync(storyId, 1, Ct));
        });

        Assert.Equal(2, generator.CallCount);
    }

    // What gpt-image-1-mini answered for page 3 of a real story on 2026-09-24 (ADR-0025), as
    // AzureOpenAIImageGenerator carries it: the status on the exception, the body in the message.
    private static HttpRequestException ModerationBlocked() =>
        new(
            """Image request to /openai/deployments/gpt-image-1-mini/images/generations failed with 400 BadRequest: { "error": { "message": "Your request was rejected by the safety system.", "type": "image_generation_user_error", "code": "moderation_blocked" } }""",
            null,
            HttpStatusCode.BadRequest
        );

    [Fact]
    public async Task Moderation_refusal_renders_the_page_from_a_rewritten_prompt()
    {
        await _factory.ResetAsync();
        var storyId = await SeedGeneratingStoryAsync();
        var generator = new FakeImageGenerator { ThrowOnGenerate = ModerationBlocked(), FailuresBeforeSuccess = 1 };

        await _factory.ExecuteDbAsync(async db =>
        {
            await new StoryPersister(db).PersistPagesAsync(storyId, Brief(), Ct);
            Assert.Equal(PageRenderOutcome.Rendered, await NewRenderer(db, generator, new InMemoryImageStore()).RenderAsync(storyId, 1, Ct));
        });

        Assert.Equal(2, generator.CallCount);
        // The second attempt draws the rewrite, with the story's style suffix composed as always.
        Assert.StartsWith(RewrittenPrompt, generator.Prompts[1], StringComparison.Ordinal);
        Assert.Contains("Art style: soft watercolour.", generator.Prompts[1], StringComparison.Ordinal);

        var page = await _factory.ExecuteDbAsync(db =>
            db.Pages.SingleAsync(p => p.StoryId == storyId && p.PageNo == 1, Ct)
        );
        Assert.Equal(PageStatus.Complete, page.Status);
        Assert.NotNull(page.ImageUrl);
        // The database records what was actually drawn...
        Assert.Equal(RewrittenPrompt, page.IllustrationPrompt);
        // ...and the story the child reads is exactly the one the parent confirmed.
        Assert.Equal("Page 1 text.", page.Content);
    }

    [Fact]
    public async Task The_rewrite_is_asked_for_with_the_refused_prompt_and_the_page_text()
    {
        await _factory.ResetAsync();
        var storyId = await SeedGeneratingStoryAsync();
        var generator = new FakeImageGenerator { ThrowOnGenerate = ModerationBlocked(), FailuresBeforeSuccess = 1 };
        var chat = new RecordingChatClient { Reply = RewrittenPrompt };

        await _factory.ExecuteDbAsync(async db =>
        {
            await new StoryPersister(db).PersistPagesAsync(storyId, Brief(), Ct);
            await NewRenderer(db, generator, new InMemoryImageStore(), chat).RenderAsync(storyId, 1, Ct);
        });

        // The page's own prompt, not the composed one: the style suffix is re-added in code.
        var request = Assert.Single(chat.Requests)[^1].Text;
        Assert.Contains("Fox, a small red fox, scene 1", request, StringComparison.Ordinal);
        Assert.DoesNotContain("Art style:", request, StringComparison.Ordinal);
        Assert.Contains("Page 1 text.", request, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refused_rewrite_fails_the_page_without_a_third_attempt()
    {
        await _factory.ResetAsync();
        var storyId = await SeedGeneratingStoryAsync();
        // Refuses every time, the rewrite included.
        var generator = new FakeImageGenerator { ThrowOnGenerate = ModerationBlocked() };
        var chat = new RecordingChatClient { Reply = RewrittenPrompt };

        await _factory.ExecuteDbAsync(async db =>
        {
            await new StoryPersister(db).PersistPagesAsync(storyId, Brief(), Ct);
            Assert.Equal(PageRenderOutcome.Refused, await NewRenderer(db, generator, new InMemoryImageStore(), chat).RenderAsync(storyId, 1, Ct));
        });

        // One rewrite, never a chain of them drifting further from the story.
        Assert.Equal(2, generator.CallCount);
        Assert.Single(chat.Requests);
        var page = await _factory.ExecuteDbAsync(db =>
            db.Pages.SingleAsync(p => p.StoryId == storyId && p.PageNo == 1, Ct)
        );
        Assert.Equal(PageStatus.Failed, page.Status);
    }

    [Fact]
    public async Task No_usable_rewrite_fails_the_page_after_one_attempt()
    {
        await _factory.ResetAsync();
        var storyId = await SeedGeneratingStoryAsync();
        var generator = new FakeImageGenerator { ThrowOnGenerate = ModerationBlocked() };

        await _factory.ExecuteDbAsync(async db =>
        {
            await new StoryPersister(db).PersistPagesAsync(storyId, Brief(), Ct);
            Assert.Equal(
                PageRenderOutcome.Refused,
                await NewRenderer(db, generator, new InMemoryImageStore(), new RecordingChatClient { Reply = "" })
                    .RenderAsync(storyId, 1, Ct)
            );
        });

        // Retrying the refused prompt unchanged would only get the same answer.
        Assert.Equal(1, generator.CallCount);
        var page = await _factory.ExecuteDbAsync(db =>
            db.Pages.SingleAsync(p => p.StoryId == storyId && p.PageNo == 1, Ct)
        );
        Assert.Equal(PageStatus.Failed, page.Status);
        Assert.Equal("Fox, a small red fox, scene 1", page.IllustrationPrompt);
    }

    [Fact]
    public async Task CompleteStory_marks_complete_when_every_page_rendered()
    {
        await _factory.ResetAsync();
        var storyId = await SeedGeneratingStoryAsync();

        await _factory.ExecuteDbAsync(async db =>
        {
            await new StoryPersister(db).PersistPagesAsync(storyId, Brief(), Ct);
            var renderer = NewRenderer(db, new FakeImageGenerator(), new InMemoryImageStore());
            for (var pageNo = 1; pageNo <= GenerationBriefValidator.PageCount; pageNo++)
            {
                await renderer.RenderAsync(storyId, pageNo, Ct);
            }
            await new StoryPersister(db).CompleteStoryAsync(storyId, Ct);
        });

        Assert.Equal(StoryStatus.Complete, await StoryStatusAsync(storyId));
    }

    // A picture book with a missing picture is not delivered (ADR-0026): five good pages are not
    // enough.
    [Fact]
    public async Task CompleteStory_marks_failed_when_one_page_failed()
    {
        await _factory.ResetAsync();
        var storyId = await SeedGeneratingStoryAsync();

        await _factory.ExecuteDbAsync(async db =>
        {
            await new StoryPersister(db).PersistPagesAsync(storyId, Brief(), Ct);
            var renderer = NewRenderer(db, new FakeImageGenerator(), new InMemoryImageStore());
            for (var pageNo = 1; pageNo <= GenerationBriefValidator.PageCount; pageNo++)
            {
                await renderer.RenderAsync(storyId, pageNo, Ct);
            }

            var page3 = await db.Pages.SingleAsync(p => p.StoryId == storyId && p.PageNo == 3, Ct);
            page3.Status = PageStatus.Failed;
            page3.ImageUrl = null;
            await db.SaveChangesAsync(Ct);

            await new StoryPersister(db).CompleteStoryAsync(storyId, Ct);
        });

        Assert.Equal(StoryStatus.Failed, await StoryStatusAsync(storyId));
    }

    // The worker stops at the first failed page (ADR-0026), leaving the rest Pending.
    [Fact]
    public async Task CompleteStory_marks_failed_when_a_page_was_never_rendered()
    {
        await _factory.ResetAsync();
        var storyId = await SeedGeneratingStoryAsync();

        await _factory.ExecuteDbAsync(async db =>
        {
            await new StoryPersister(db).PersistPagesAsync(storyId, Brief(), Ct);
            var renderer = NewRenderer(db, new FakeImageGenerator(), new InMemoryImageStore());
            for (var pageNo = 1; pageNo < GenerationBriefValidator.PageCount; pageNo++)
            {
                await renderer.RenderAsync(storyId, pageNo, Ct);
            }
            await new StoryPersister(db).CompleteStoryAsync(storyId, Ct);
        });

        Assert.Equal(StoryStatus.Failed, await StoryStatusAsync(storyId));
    }

    [Fact]
    public async Task Generate_renders_every_page_and_completes_the_story()
    {
        await _factory.ResetAsync();
        var storyId = await SeedGeneratingStoryAsync();
        var images = new FakeImageGenerator();

        await _factory.ExecuteDbAsync(db => NewGenerator(db, images).GenerateAsync(storyId, Brief(), Ct));

        Assert.Equal(GenerationBriefValidator.PageCount, images.CallCount);
        Assert.Equal(StoryStatus.Complete, await StoryStatusAsync(storyId));
    }

    // Once a page has failed, even after its rewrite (ADR-0025), the story is Failed whatever
    // happens next, and a failed story can only be deleted: the pages after it would be paid for
    // and never seen (ADR-0026).
    [Fact]
    public async Task Generate_stops_at_the_first_failed_page_and_fails_the_story()
    {
        await _factory.ResetAsync();
        var storyId = await SeedGeneratingStoryAsync();
        // Refuses everything, the rewrite included, so page 1 fails.
        var images = new FakeImageGenerator { ThrowOnGenerate = ModerationBlocked() };

        await _factory.ExecuteDbAsync(db => NewGenerator(db, images).GenerateAsync(storyId, Brief(), Ct));

        // Page 1's original prompt and its rewrite; nothing for pages 2 to 6.
        Assert.Equal(2, images.CallCount);
        var statuses = await _factory.ExecuteDbAsync(db =>
            db.Pages.Where(p => p.StoryId == storyId).OrderBy(p => p.PageNo).Select(p => p.Status).ToListAsync(Ct)
        );
        Assert.Equal(
            [PageStatus.Failed, PageStatus.Pending, PageStatus.Pending, PageStatus.Pending, PageStatus.Pending, PageStatus.Pending],
            statuses
        );
        Assert.Equal(StoryStatus.Failed, await StoryStatusAsync(storyId));
    }

    private static StoryGenerator NewGenerator(StoryWalkerDbContext db, FakeImageGenerator images) =>
        new(
            new StoryPersister(db),
            NewRenderer(db, images, new InMemoryImageStore()),
            NullLogger<StoryGenerator>.Instance
        );

    private Task<StoryStatus> StoryStatusAsync(Guid storyId) =>
        _factory.ExecuteDbAsync(db =>
            db.Stories.Where(s => s.Id == storyId).Select(s => s.Status).SingleAsync(Ct)
        );

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

    /// <summary>What the stub chat model answers when the renderer asks for a rewrite (ADR-0025).</summary>
    private const string RewrittenPrompt = "Fox, a small red fox, sitting calmly under the moon";

    private static PageImageRenderer NewRenderer(
        StoryWalkerDbContext db,
        IImageGenerator generator,
        InMemoryImageStore store,
        RecordingChatClient? chat = null
    ) =>
        new(
            db,
            generator,
            store,
            new IllustrationPromptRewriter(
                chat ?? new RecordingChatClient { Reply = RewrittenPrompt },
                NullLogger<IllustrationPromptRewriter>.Instance
            ),
            new StubHttpClientFactory(),
            NullLogger<PageImageRenderer>.Instance
        )
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
