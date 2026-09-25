using StoryWalker.Domain.Enums;

namespace StoryWalker.Application.Quota;

/// <summary>
/// The weekly story limit's rules (ADR-0030): how many stories a plan allows, and where the
/// calendar week starts. Pure, so the boundaries can be tested without a database or a clock.
/// </summary>
public static class StoryQuota
{
    /// <summary>
    /// Finished stories a plan allows per calendar week. Only <see cref="UserTier.Free"/> exists; a
    /// new plan fails loudly here until someone decides its limit, rather than inheriting Free's.
    /// </summary>
    public static int WeeklyLimitFor(UserTier tier) =>
        tier switch
        {
            UserTier.Free => 7,
            _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "No weekly limit set for this plan."),
        };

    /// <summary>
    /// The Monday 00:00 UTC that starts the week containing <paramref name="utcNow"/>. UTC because
    /// we do not know the user's time zone; the dashboard shows the reset in local time.
    /// </summary>
    public static DateTime WeekStart(DateTime utcNow)
    {
        // DayOfWeek counts from Sunday; this counts days since Monday.
        var daysSinceMonday = ((int)utcNow.DayOfWeek + 6) % 7;
        return DateTime.SpecifyKind(utcNow.Date.AddDays(-daysSinceMonday), DateTimeKind.Utc);
    }
}
