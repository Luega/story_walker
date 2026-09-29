using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using StoryWalker.Infrastructure.Generation;

namespace StoryWalker.Functions.Functions;

/// <summary>
/// Every 15 minutes, fails the stories whose generation message was lost — dead-lettered after ten
/// deliveries, or expired in the queue — and reports them to their conversation (ADR-0031). Nothing
/// reads the dead-letter queue; this settles the story instead, which is what the parent sees.
/// <para>
/// A thin shell over <see cref="StaleGenerationSweeper"/>, tested against a real database like the
/// rest of the worker (ADR-0017). A tick that throws is simply redone by the next one.
/// </para>
/// </summary>
public sealed class FailStaleGenerations(
    StaleGenerationSweeper sweeper,
    ILogger<FailStaleGenerations> logger
)
{
    [Function(nameof(FailStaleGenerations))]
    public async Task Run([TimerTrigger("0 */15 * * * *")] TimerInfo timer, CancellationToken ct)
    {
        var failed = await sweeper.SweepAsync(ct);
        if (failed > 0)
        {
            logger.LogWarning("Failed {Count} stories stuck in Generating.", failed);
        }
    }
}
