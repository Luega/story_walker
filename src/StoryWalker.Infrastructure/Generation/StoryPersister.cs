using Microsoft.EntityFrameworkCore;
using StoryWalker.Application.Brief;
using StoryWalker.Domain.Entities;
using StoryWalker.Domain.Enums;
using StoryWalker.Infrastructure.Persistence;

namespace StoryWalker.Infrastructure.Generation;

/// <summary>
/// Writes the brief's pages and settles the story's final status. The Durable activities that call
/// this are two-line shells, so the logic worth testing lives here and is exercised against a real
/// database in the API test suite rather than through a Durable test harness (ADR-0017).
/// </summary>
public sealed class StoryPersister(StoryWalkerDbContext db)
{
    /// <summary>
    /// Writes one <see cref="Page"/> per brief page, all <see cref="PageStatus.Pending"/> until the
    /// renderer fills in the image. Idempotent (ADR-0005): a redelivered message that replays this
    /// activity finds the pages already there and does nothing.
    /// </summary>
    public async Task PersistPagesAsync(Guid storyId, GenerationBrief brief, CancellationToken ct = default)
    {
        if (await db.Pages.AnyAsync(p => p.StoryId == storyId, ct))
        {
            return;
        }

        db.Pages.AddRange(
            brief.Pages.Select(p => new Page
            {
                Id = Guid.NewGuid(),
                StoryId = storyId,
                PageNo = p.PageNo,
                Content = p.Text,
                IllustrationPrompt = p.IllustrationPrompt,
                Status = PageStatus.Pending,
            })
        );

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Settles the story once every page has been attempted. A story whose pages all failed to
    /// render is <see cref="StoryStatus.Failed"/>; otherwise it is
    /// <see cref="StoryStatus.Complete"/> even if individual pages failed, because a story with
    /// five of six pictures is still readable — the per-page
    /// <see cref="PageStatus"/> is what tells a client which illustration is missing.
    /// </summary>
    public async Task CompleteStoryAsync(Guid storyId, CancellationToken ct = default)
    {
        var story = await db.Stories.FirstOrDefaultAsync(s => s.Id == storyId, ct);
        if (story is null)
        {
            return;
        }

        var anyPageRendered = await db.Pages.AnyAsync(
            p => p.StoryId == storyId && p.Status == PageStatus.Complete,
            ct
        );

        story.Status = anyPageRendered ? StoryStatus.Complete : StoryStatus.Failed;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Marks the whole story failed; used when the orchestration itself blows up.</summary>
    public async Task FailStoryAsync(Guid storyId, CancellationToken ct = default)
    {
        var story = await db.Stories.FirstOrDefaultAsync(s => s.Id == storyId, ct);
        if (story is null)
        {
            return;
        }

        story.Status = StoryStatus.Failed;
        await db.SaveChangesAsync(ct);
    }
}
