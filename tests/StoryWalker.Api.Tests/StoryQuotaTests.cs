using StoryWalker.Application.Quota;
using StoryWalker.Domain.Enums;

namespace StoryWalker.Api.Tests;

/// <summary>
/// Unit tests for <see cref="StoryQuota"/>: the plan's limit and where the calendar week starts
/// (ADR-0030). The week is Monday 00:00 to Monday 00:00 UTC.
/// </summary>
public sealed class StoryQuotaTests
{
    [Fact]
    public void The_free_plan_allows_seven_stories_a_week()
    {
        Assert.Equal(7, StoryQuota.WeeklyLimitFor(UserTier.Free));
    }

    [Theory]
    // Thursday 2026-09-24 15:30 → Monday 2026-09-21.
    [InlineData("2026-09-24T15:30:00Z", "2026-09-21T00:00:00Z")]
    // Monday at midnight is already the new week.
    [InlineData("2026-09-21T00:00:00Z", "2026-09-21T00:00:00Z")]
    // Sunday one second before midnight is still the old one.
    [InlineData("2026-09-27T23:59:59Z", "2026-09-21T00:00:00Z")]
    // Across a month boundary.
    [InlineData("2026-10-02T08:00:00Z", "2026-09-28T00:00:00Z")]
    public void The_week_starts_on_monday_at_midnight_utc(string now, string expected)
    {
        var start = StoryQuota.WeekStart(DateTime.Parse(now).ToUniversalTime());

        Assert.Equal(DateTime.Parse(expected).ToUniversalTime(), start);
        Assert.Equal(DateTimeKind.Utc, start.Kind);
    }
}
