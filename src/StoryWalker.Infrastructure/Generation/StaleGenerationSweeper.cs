using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StoryWalker.Application.Agent;
using StoryWalker.Domain.Enums;
using StoryWalker.Infrastructure.Persistence;

namespace StoryWalker.Infrastructure.Generation;

/// <summary>
/// Settles the stories whose message never reached the end of a run (ADR-0031). A message
/// dead-lettered after ten deliveries, or expired in the queue, leaves its story
/// <see cref="StoryStatus.Generating"/> for ever: nothing reads the dead-letter queue. This fixes
/// the story rather than the message, so it covers both, and whatever else loses one.
/// <para>
/// Reported to the conversation first, then failed: if the sweep dies between the two, the next
/// one finds the story still generating and tries again, and the recorder writes its report only
/// once (ADR-0027).
/// </para>
/// </summary>
public sealed class StaleGenerationSweeper(
    StoryWalkerDbContext db,
    StoryPersister persister,
    GenerationFailureRecorder failureRecorder,
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

        foreach (var storyId in stale)
        {
            // The first page without a picture is where the run stopped; with no pages at all, the
            // brief was never even written.
            var stoppedAt = await db
                .Pages.Where(p => p.StoryId == storyId && p.Status != PageStatus.Complete)
                .OrderBy(p => p.PageNo)
                .Select(p => (int?)p.PageNo)
                .FirstOrDefaultAsync(ct);

            logger.LogWarning(
                "Story {StoryId} is still generating after {Threshold}; its message was lost. Failing it.",
                storyId,
                Threshold
            );

            await failureRecorder.RecordAsync(
                storyId,
                stoppedAt ?? 1,
                GenerationFailureReason.TechnicalProblem,
                ct
            );
            await persister.FailStoryAsync(storyId, ct);
        }

        return stale.Count;
    }
}
