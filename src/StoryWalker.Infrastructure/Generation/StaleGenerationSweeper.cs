using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StoryWalker.Domain.Enums;
using StoryWalker.Infrastructure.Persistence;

namespace StoryWalker.Infrastructure.Generation;

/// <summary>
/// The backstop for a story whose message was lost (ADR-0031): fails every story still
/// <see cref="StoryStatus.Generating"/> long after any run could be going. Dead-lettered and
/// expired messages are normally settled at once from the dead-letter queue (ADR-0032); this
/// catches whatever loses a message without dead-lettering it, or a dead-letter function that is
/// down. It fixes the story rather than the message, so it does not care which.
/// </summary>
public sealed class StaleGenerationSweeper(
    StoryWalkerDbContext db,
    LostGenerationSettler settler,
    TimeProvider time,
    ILogger<StaleGenerationSweeper> logger
)
{
    /// <summary>
    /// How long after it was queued a story is given up. A message expires one hour after it is
    /// sent, so no run starts later than that, and a run lasts at most <c>functionTimeout</c>
    /// (30 minutes); the rest is margin. Grows with either of them.
    /// </summary>
    public static readonly TimeSpan Threshold = TimeSpan.FromHours(2);

    /// <returns>How many stories were failed.</returns>
    public async Task<int> SweepAsync(CancellationToken ct = default)
    {
        var queuedBefore = time.GetUtcNow().UtcDateTime - Threshold;
        var stale = await db
            .Stories.Where(s => s.Status == StoryStatus.Generating && s.CreatedAt < queuedBefore)
            .Select(s => s.Id)
            .ToListAsync(ct);

        var failed = 0;
        foreach (var storyId in stale)
        {
            logger.LogWarning(
                "Story {StoryId} is still generating after {Threshold}; its message was lost. Failing it.",
                storyId,
                Threshold
            );

            if (await settler.SettleAsync(storyId, ct))
            {
                failed++;
            }
        }

        return failed;
    }
}
