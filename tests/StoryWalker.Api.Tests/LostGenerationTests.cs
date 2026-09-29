using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StoryWalker.Application.Agent;
using StoryWalker.Application.Brief;
using StoryWalker.Domain.Entities;
using StoryWalker.Domain.Enums;
using StoryWalker.Infrastructure.Generation;
using StoryWalker.Infrastructure.Persistence;

namespace StoryWalker.Api.Tests;

/// <summary>
/// Tests the handling of a story whose message was lost, against the real Postgres.
/// <see cref="LostGenerationSettler"/> fails one story and reports it to its conversation; the
/// dead-letter function calls it for each dead-lettered message (ADR-0032), and
/// <see cref="StaleGenerationSweeper"/> for each story still <see cref="StoryStatus.Generating"/>
/// long after any run could be going (ADR-0031). <c>CreatedAt</c> is stamped by the DbContext, so
/// the sweeper tests move the clock rather than the story.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class LostGenerationTests(StoryWalkerApiFactory factory)
{
    private const string Advice = "Something went wrong on our side. Shall we try again?";

    private readonly StoryWalkerApiFactory _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTimeOffset Later(TimeSpan after) => DateTimeOffset.UtcNow + after;

    [Fact]
    public async Task Settle_fails_a_generating_story_and_reports_it()
    {
        var (storyId, conversationId) = await SeedGeneratingStoryAsync(renderedPages: 2);

        var settled = await SettleAsync(storyId);

        Assert.True(settled);
        Assert.Equal(StoryStatus.Failed, await StatusAsync(storyId));
        var messages = await MessagesAsync(conversationId);
        Assert.Contains("A technical problem stopped the illustrations at page 3", messages[^2].Content, StringComparison.Ordinal);
        Assert.Equal(Advice, messages[^1].Content);
    }

    // The sweeper and the dead-letter function can both reach the same story; the second is a no-op.
    // So is a story the worker did settle before its message was lost.
    [Theory]
    [InlineData(StoryStatus.Complete)]
    [InlineData(StoryStatus.Failed)]
    public async Task Settle_leaves_a_settled_story_alone(StoryStatus status)
    {
        var (storyId, conversationId) = await SeedGeneratingStoryAsync(renderedPages: 2);
        await _factory.ExecuteDbAsync(db =>
            db.Stories.Where(s => s.Id == storyId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status), Ct)
        );

        var settled = await SettleAsync(storyId);

        Assert.False(settled);
        Assert.Equal(status, await StatusAsync(storyId));
        Assert.Equal(3, (await MessagesAsync(conversationId)).Count);
    }

    // The story may have been deleted since it was queued.
    [Fact]
    public async Task Settle_ignores_an_unknown_story()
    {
        await _factory.ResetAsync();

        Assert.False(await SettleAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Fails_a_story_generating_for_longer_than_the_threshold()
    {
        var (storyId, _) = await SeedGeneratingStoryAsync(renderedPages: 2);

        var swept = await SweepAsync(Later(StaleGenerationSweeper.Threshold + TimeSpan.FromMinutes(1)));

        Assert.Equal(1, swept);
        Assert.Equal(StoryStatus.Failed, await StatusAsync(storyId));
    }

    // Reported like any other failure, so the parent can pick the conversation up again (ADR-0027),
    // naming the first page without a picture.
    [Fact]
    public async Task Reports_a_technical_problem_at_the_first_unrendered_page()
    {
        var (_, conversationId) = await SeedGeneratingStoryAsync(renderedPages: 2);

        await SweepAsync(Later(StaleGenerationSweeper.Threshold + TimeSpan.FromMinutes(1)));

        var messages = await MessagesAsync(conversationId);
        Assert.Equal(MessageRole.System, messages[^2].Role);
        Assert.Contains("A technical problem stopped the illustrations at page 3", messages[^2].Content, StringComparison.Ordinal);
        Assert.Contains("Page 3 text.", messages[^2].Content, StringComparison.Ordinal);
        Assert.Equal(Advice, messages[^1].Content);
        Assert.Equal(ConversationStatus.Active, await ConversationStatusAsync(conversationId));
    }

    // A message dead-lettered before its first page was written leaves a story with no pages.
    [Fact]
    public async Task Reports_page_1_when_the_pages_were_never_written()
    {
        var (storyId, conversationId) = await SeedGeneratingStoryAsync(renderedPages: null);

        await SweepAsync(Later(StaleGenerationSweeper.Threshold + TimeSpan.FromMinutes(1)));

        Assert.Equal(StoryStatus.Failed, await StatusAsync(storyId));
        Assert.Contains("at page 1", (await MessagesAsync(conversationId))[^2].Content, StringComparison.Ordinal);
    }

    // A run can still be going within the threshold; the sweeper must not fail it under its feet.
    [Fact]
    public async Task Leaves_a_story_younger_than_the_threshold_alone()
    {
        var (storyId, conversationId) = await SeedGeneratingStoryAsync(renderedPages: 2);

        var swept = await SweepAsync(Later(StaleGenerationSweeper.Threshold - TimeSpan.FromMinutes(1)));

        Assert.Equal(0, swept);
        Assert.Equal(StoryStatus.Generating, await StatusAsync(storyId));
        Assert.Equal(ConversationStatus.Completed, await ConversationStatusAsync(conversationId));
    }

    [Theory]
    [InlineData(StoryStatus.Complete)]
    [InlineData(StoryStatus.Failed)]
    public async Task Leaves_a_settled_story_alone(StoryStatus status)
    {
        var (storyId, conversationId) = await SeedGeneratingStoryAsync(renderedPages: 2);
        await _factory.ExecuteDbAsync(db =>
            db.Stories.Where(s => s.Id == storyId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status), Ct)
        );

        var swept = await SweepAsync(Later(StaleGenerationSweeper.Threshold + TimeSpan.FromMinutes(1)));

        Assert.Equal(0, swept);
        Assert.Equal(status, await StatusAsync(storyId));
        Assert.Equal(3, (await MessagesAsync(conversationId)).Count);
    }

    // The next tick after a sweep finds nothing left to do.
    [Fact]
    public async Task Sweeps_each_story_once()
    {
        var (_, conversationId) = await SeedGeneratingStoryAsync(renderedPages: 2);
        var now = Later(StaleGenerationSweeper.Threshold + TimeSpan.FromMinutes(1));

        await SweepAsync(now);
        var second = await SweepAsync(now + TimeSpan.FromMinutes(15));

        Assert.Equal(0, second);
        Assert.Equal(5, (await MessagesAsync(conversationId)).Count);
    }

    private Task<bool> SettleAsync(Guid storyId) =>
        _factory.ExecuteDbAsync(db => Settler(db).SettleAsync(storyId, Ct));

    private Task<int> SweepAsync(DateTimeOffset now) =>
        _factory.ExecuteDbAsync(db =>
            new StaleGenerationSweeper(
                db,
                Settler(db),
                new FixedTimeProvider(now),
                NullLogger<StaleGenerationSweeper>.Instance
            ).SweepAsync(Ct)
        );

    private static LostGenerationSettler Settler(StoryWalkerDbContext db) =>
        new(
            db,
            new StoryPersister(db),
            new GenerationFailureRecorder(
                db,
                new RecordingChatClient { Reply = Advice },
                NullLogger<GenerationFailureRecorder>.Instance
            )
        );

    /// <summary>
    /// A confirmed conversation and the Generating story it queued. With
    /// <paramref name="renderedPages"/> set, the pages are written and that many have a picture;
    /// null leaves the story without pages.
    /// </summary>
    private async Task<(Guid StoryId, Guid ConversationId)> SeedGeneratingStoryAsync(int? renderedPages)
    {
        await _factory.ResetAsync();
        var userId = await _factory.ExecuteDbAsync(db => SeedData.SeedUserAsync(db, "user_sweeper_tests"));
        var conversationId = Guid.NewGuid();
        var storyId = Guid.NewGuid();

        await _factory.ExecuteDbAsync(async db =>
        {
            db.Conversations.Add(
                new Conversation
                {
                    Id = conversationId,
                    UserId = userId,
                    Status = ConversationStatus.Completed,
                }
            );
            string[] contents =
            [
                StoryAgentPrompt.Instructions,
                "A story about a fox and the moon, please.",
                $"Generating your story now. {StoryAgentPrompt.EndOfChatSentinel}",
            ];
            MessageRole[] roles = [MessageRole.System, MessageRole.User, MessageRole.Assistant];
            for (var i = 0; i < contents.Length; i++)
            {
                db.ConversationMessages.Add(
                    new ConversationMessage
                    {
                        Id = Guid.NewGuid(),
                        ConversationId = conversationId,
                        Seq = i,
                        Role = roles[i],
                        Content = contents[i],
                    }
                );
            }

            db.Stories.Add(
                new Story
                {
                    Id = storyId,
                    UserId = userId,
                    ConversationId = conversationId,
                    Title = "The Fox and the Moon",
                    TargetAge = 6,
                    ArtStyle = "soft watercolour",
                    Status = StoryStatus.Generating,
                }
            );
            await db.SaveChangesAsync(Ct);

            if (renderedPages is { } rendered)
            {
                await new StoryPersister(db).PersistPagesAsync(storyId, Brief(), Ct);
                await db
                    .Pages.Where(p => p.StoryId == storyId && p.PageNo <= rendered)
                    .ExecuteUpdateAsync(
                        p => p.SetProperty(x => x.Status, PageStatus.Complete)
                            .SetProperty(x => x.ImageUrl, x => "https://images.test/" + x.PageNo + ".png"),
                        Ct
                    );
            }
        });

        return (storyId, conversationId);
    }

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

    private Task<StoryStatus> StatusAsync(Guid storyId) =>
        _factory.ExecuteDbAsync(db => db.Stories.Where(s => s.Id == storyId).Select(s => s.Status).SingleAsync(Ct));

    private Task<List<ConversationMessage>> MessagesAsync(Guid conversationId) =>
        _factory.ExecuteDbAsync(db =>
            db.ConversationMessages.Where(m => m.ConversationId == conversationId).OrderBy(m => m.Seq).ToListAsync(Ct)
        );

    private Task<ConversationStatus> ConversationStatusAsync(Guid conversationId) =>
        _factory.ExecuteDbAsync(db =>
            db.Conversations.Where(c => c.Id == conversationId).Select(c => c.Status).SingleAsync(Ct)
        );
}
