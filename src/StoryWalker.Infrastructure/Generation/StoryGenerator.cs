using Microsoft.Extensions.Logging;
using StoryWalker.Application.Agent;
using StoryWalker.Application.Brief;

namespace StoryWalker.Infrastructure.Generation;

/// <summary>
/// The whole generation run for one brief: persist the pages, render them in order, settle the
/// story. It lives here rather than in the triggered function so the run can be tested as an
/// ordinary service against a real database (ADR-0017); the function is a shell around it.
/// </summary>
public sealed class StoryGenerator(
    StoryPersister persister,
    PageImageRenderer renderer,
    GenerationFailureRecorder failureRecorder,
    ILogger<StoryGenerator> logger
)
{
    public async Task GenerateAsync(Guid storyId, GenerationBrief brief, CancellationToken ct = default)
    {
        await persister.PersistPagesAsync(storyId, brief, ct);

        // Serial, not parallel: six concurrent calls against an image deployment on default quota
        // produce a 429 storm, and six sequential renders take about the "few minutes" the agent
        // already promised the user (ADR-0019).
        (int PageNo, PageRenderOutcome Outcome)? stoppedAt = null;
        foreach (var page in brief.Pages.OrderBy(p => p.PageNo))
        {
            var outcome = await renderer.RenderAsync(storyId, page.PageNo, ct);
            logger.LogInformation(
                "Story {StoryId} page {PageNo}: {Outcome}.",
                storyId,
                page.PageNo,
                outcome
            );

            // A failed page (already past its moderation rewrite, ADR-0025) fails the story, and a
            // failed story can only be deleted: stop rather than pay for pictures nobody will see
            // (ADR-0026). The remaining pages stay Pending, which settles the story Failed.
            if (outcome != PageRenderOutcome.Rendered)
            {
                logger.LogWarning(
                    "Story {StoryId} stopped at page {PageNo}; it will be settled Failed.",
                    storyId,
                    page.PageNo
                );
                stoppedAt = (page.PageNo, outcome);
                break;
            }
        }

        await persister.CompleteStoryAsync(storyId, ct);
        logger.LogInformation("Settled final status for story {StoryId}.", storyId);

        // Tell the conversation that queued the story, so the parent can pick it up from the
        // confirmation with the agent's advice (ADR-0027).
        if (stoppedAt is var (pageNo, failure))
        {
            await failureRecorder.RecordAsync(
                storyId,
                pageNo,
                failure == PageRenderOutcome.Refused
                    ? GenerationFailureReason.ImageRefused
                    : GenerationFailureReason.TechnicalProblem,
                ct
            );
        }
    }
}
