using Microsoft.EntityFrameworkCore;
using StoryWalker.Domain.Entities;
using StoryWalker.Domain.Enums;
using StoryWalker.Infrastructure.Persistence;
using StoryWalker.Infrastructure.Quota;

namespace StoryWalker.Api.Tests;

/// <summary>
/// Tests <see cref="StoryQuotaService"/> against the real Postgres (ADR-0030): what counts against
/// the weekly limit, with the clock stopped on Thursday 2026-09-24, in the week that started on
/// Monday 2026-09-21.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class StoryQuotaServiceTests(StoryWalkerApiFactory factory)
{
    private static readonly DateTime Now = new(2026, 9, 24, 15, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime ThisMonday = new(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc);

    private readonly StoryWalkerApiFactory _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Counts_the_stories_finished_this_week_and_says_when_it_resets()
    {
        var me = await ResetAndSeedUserAsync("user_quota_tests");
        await LedgerAsync(me, ThisMonday.AddHours(1));
        await LedgerAsync(me, Now.AddMinutes(-5));
        // Last week's, a second before Monday.
        await LedgerAsync(me, ThisMonday.AddSeconds(-1));

        var quota = await QuotaAsync(me);

        Assert.Equal(7, quota.Limit);
        Assert.Equal(2, quota.Used);
        Assert.Equal(5, quota.Remaining);
        Assert.Equal(ThisMonday.AddDays(7), quota.ResetsAt);
    }

    // Deleting a story does not give its slot back: its ledger row stays, unlinked.
    [Fact]
    public async Task A_deleted_story_still_counts()
    {
        var me = await ResetAndSeedUserAsync("user_quota_tests");
        await LedgerAsync(me, Now.AddHours(-1), storyId: null);

        Assert.Equal(1, (await QuotaAsync(me)).Used);
    }

    // A story in flight holds a slot, or six finished and two generating would end at eight; a
    // failed one holds nothing.
    [Fact]
    public async Task A_story_still_generating_holds_a_slot_and_a_failed_one_does_not()
    {
        var me = await ResetAndSeedUserAsync("user_quota_tests");
        await StoryAsync(me, StoryStatus.Generating);
        await StoryAsync(me, StoryStatus.Failed);

        Assert.Equal(1, (await QuotaAsync(me)).Used);
    }

    [Fact]
    public async Task Another_users_stories_do_not_count()
    {
        var me = await ResetAndSeedUserAsync("user_quota_tests");
        var other = await _factory.ExecuteDbAsync(db => SeedData.SeedUserAsync(db, "user_quota_other"));
        await LedgerAsync(other, Now.AddHours(-1));
        await StoryAsync(other, StoryStatus.Generating);

        Assert.Equal(0, (await QuotaAsync(me)).Used);
    }

    [Fact]
    public async Task Nothing_remains_past_the_limit_rather_than_a_negative_count()
    {
        var me = await ResetAndSeedUserAsync("user_quota_tests");
        for (var i = 0; i < 8; i++)
        {
            await LedgerAsync(me, Now.AddMinutes(-i - 1));
        }

        var quota = await QuotaAsync(me);

        Assert.Equal(8, quota.Used);
        Assert.Equal(0, quota.Remaining);
    }

    private Task<Application.Api.QuotaDto> QuotaAsync(Guid userId) =>
        _factory.ExecuteDbAsync(db =>
            new StoryQuotaService(db, new FixedTimeProvider(new DateTimeOffset(Now))).GetAsync(userId, Ct)
        );

    private async Task<Guid> ResetAndSeedUserAsync(string clerkUserId)
    {
        await _factory.ResetAsync();
        return await _factory.ExecuteDbAsync(db => SeedData.SeedUserAsync(db, clerkUserId));
    }

    private Task LedgerAsync(Guid userId, DateTime completedAt, Guid? storyId = default) =>
        _factory.ExecuteDbAsync(async db =>
        {
            db.CompletedGenerations.Add(
                new CompletedGeneration
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    StoryId = storyId,
                    CompletedAt = completedAt,
                }
            );
            await db.SaveChangesAsync(Ct);
        });

    private Task StoryAsync(Guid userId, StoryStatus status) =>
        _factory.ExecuteDbAsync(async db =>
        {
            db.Stories.Add(
                new Story
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Title = "A story",
                    TargetAge = 6,
                    ArtStyle = "watercolour",
                    Status = status,
                }
            );
            await db.SaveChangesAsync(Ct);
        });
}
