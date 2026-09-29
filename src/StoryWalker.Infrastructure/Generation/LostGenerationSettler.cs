using Microsoft.EntityFrameworkCore;
using StoryWalker.Application.Agent;
using StoryWalker.Domain.Enums;
using StoryWalker.Infrastructure.Persistence;

namespace StoryWalker.Infrastructure.Generation;

/// <summary>
/// Settles one story whose message never reached the end of a run: dead-lettered after ten
/// deliveries, or expired in the queue. Called for each dead-lettered message (ADR-0032) and, as the
/// backstop, for each story the timer finds still generating (ADR-0031).
/// <para>
/// Only a story still <see cref="StoryStatus.Generating"/> is touched, so whichever of the two
/// reaches it second does nothing, and so does a story the worker did settle. Reported to the
/// conversation first, then failed: if this dies between the two, the next attempt finds the story
/// still generating and tries again, and the recorder writes its report only once (ADR-0027).
/// </para>
/// </summary>
public sealed class LostGenerationSettler(
    StoryWalkerDbContext db,
    StoryPersister persister,
    GenerationFailureRecorder failureRecorder
)
{
    /// <returns>Whether the story was failed; false when it no longer needed settling.</returns>
    public async Task<bool> SettleAsync(Guid storyId, CancellationToken ct = default)
    {
        var generating = await db.Stories.AnyAsync(
            s => s.Id == storyId && s.Status == StoryStatus.Generating,
            ct
        );
        if (!generating)
        {
            return false;
        }

        // The first page without a picture is where the run stopped; with no pages at all, the
        // brief was never even written.
        var stoppedAt = await db
            .Pages.Where(p => p.StoryId == storyId && p.Status != PageStatus.Complete)
            .OrderBy(p => p.PageNo)
            .Select(p => (int?)p.PageNo)
            .FirstOrDefaultAsync(ct);

        await failureRecorder.RecordAsync(
            storyId,
            stoppedAt ?? 1,
            GenerationFailureReason.TechnicalProblem,
            ct
        );
        await persister.FailStoryAsync(storyId, ct);
        return true;
    }
}
