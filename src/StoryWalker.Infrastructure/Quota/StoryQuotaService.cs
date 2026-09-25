using Microsoft.EntityFrameworkCore;
using StoryWalker.Application.Api;
using StoryWalker.Application.Quota;
using StoryWalker.Domain.Enums;
using StoryWalker.Infrastructure.Persistence;

namespace StoryWalker.Infrastructure.Quota;

/// <summary>
/// Where a user stands against the weekly story limit (ADR-0030). Used counts two things: the
/// ledger rows finished since Monday, which stay even when their story is deleted, and the user's
/// stories still generating, which hold a slot until they finish or fail.
/// </summary>
public sealed class StoryQuotaService(StoryWalkerDbContext db, TimeProvider time)
{
    public async Task<QuotaDto> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var weekStart = StoryQuota.WeekStart(time.GetUtcNow().UtcDateTime);

        var tier = await db.Users.Where(u => u.Id == userId).Select(u => u.Tier).FirstAsync(ct);
        var finished = await db.CompletedGenerations.CountAsync(
            g => g.UserId == userId && g.CompletedAt >= weekStart,
            ct
        );
        var generating = await db.Stories.CountAsync(
            s => s.UserId == userId && s.Status == StoryStatus.Generating,
            ct
        );

        var limit = StoryQuota.WeeklyLimitFor(tier);
        var used = finished + generating;
        return new QuotaDto(limit, used, Math.Max(0, limit - used), weekStart.AddDays(7));
    }
}
