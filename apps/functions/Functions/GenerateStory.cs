using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using StoryWalker.Application.Brief;
using StoryWalker.Application.Messaging;
using StoryWalker.Infrastructure.Generation;

namespace StoryWalker.Functions.Functions;

/// <summary>
/// The generation worker: one Service Bus-triggered function that persists the brief's pages,
/// renders an illustration for each, and settles the story's status (ADR-0020).
/// <para>
/// There is no orchestrator. The pipeline is resumable because every step is idempotent
/// (ADR-0005) and the <b>database is the checkpoint</b>: pages that exist are not written again,
/// and pages that already have an image are not re-rendered. So if this function dies halfway —
/// crash, restart, timeout — Service Bus redelivers the message (the queue allows ten deliveries)
/// and the next run continues from the first page without a picture, rather than starting over.
/// </para>
/// <para>
/// Duplicate delivery is safe for the same reason: a redelivered message re-runs a function whose
/// every step is a no-op once its work is done.
/// </para>
/// </summary>
public sealed class GenerateStory(
    StoryPersister persister,
    PageImageRenderer renderer,
    ILogger<GenerateStory> logger
)
{
    [Function(nameof(GenerateStory))]
    public async Task Run(
        [ServiceBusTrigger(QueueNames.Ingestion, Connection = "ServiceBusConnection")]
            ServiceBusReceivedMessage message,
        CancellationToken ct
    )
    {
        // The story id travels as the message id (ADR-0003), which is also what makes redelivery
        // map onto the same rows.
        var storyId = Guid.Parse(message.MessageId);
        var brief = BriefSerialization.Deserialize(message.Body.ToString());

        logger.LogInformation(
            "Generating story {StoryId} ('{Title}'), {PageCount} pages.",
            storyId,
            brief.Title,
            brief.Pages.Count
        );

        await persister.PersistPagesAsync(storyId, brief, ct);

        // Serial, not parallel: six concurrent calls against an image deployment on default quota
        // produce a 429 storm, and six sequential renders take about the "few minutes" the agent
        // already promised the user (ADR-0019).
        foreach (var page in brief.Pages.OrderBy(p => p.PageNo))
        {
            var rendered = await renderer.RenderAsync(storyId, page.PageNo, ct);
            logger.LogInformation(
                "Story {StoryId} page {PageNo}: {Outcome}.",
                storyId,
                page.PageNo,
                rendered ? "image ready" : "no image"
            );
        }

        await persister.CompleteStoryAsync(storyId, ct);
        logger.LogInformation("Settled final status for story {StoryId}.", storyId);
    }
}
