using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using StoryWalker.Application.Messaging;
using StoryWalker.Infrastructure.Generation;

namespace StoryWalker.Functions.Functions;

/// <summary>
/// Reads the ingestion queue's dead letters (ADR-0032): messages that failed ten deliveries, or
/// expired before a run picked them up. Each one's story is failed and reported to its
/// conversation at once, rather than waiting for the timer (ADR-0031).
/// <para>
/// Returning normally completes the message, which removes it: it is a copy of a brief about
/// someone's child, and the dead-letter queue would keep it for ever. It is never replayed; the
/// parent tries again from the conversation. Throwing abandons it, so it is tried again.
/// </para>
/// </summary>
public sealed class SettleDeadLetteredGeneration(
    LostGenerationSettler settler,
    ILogger<SettleDeadLetteredGeneration> logger
)
{
    [Function(nameof(SettleDeadLetteredGeneration))]
    public async Task Run(
        [ServiceBusTrigger(QueueNames.IngestionDeadLetter, Connection = "ServiceBusConnection")]
            ServiceBusReceivedMessage message,
        CancellationToken ct
    )
    {
        // Service Bus's own reason and the delivery count, never the body (ADR-0023): it is the
        // brief. The reason is what tells a crash loop (MaxDeliveryCountExceeded) from a worker
        // that was down (TTLExpiredException).
        if (!Guid.TryParse(message.MessageId, out var storyId))
        {
            logger.LogError(
                "Dead-lettered message {MessageId} is not tied to a story ({Reason}: {Description}); removing it.",
                message.MessageId,
                message.DeadLetterReason,
                message.DeadLetterErrorDescription
            );
            return;
        }

        var failed = await settler.SettleAsync(storyId, ct);
        logger.LogWarning(
            "Generation of story {StoryId} was dead-lettered after {DeliveryCount} deliveries ({Reason}: {Description}); {Outcome}.",
            storyId,
            message.DeliveryCount,
            message.DeadLetterReason,
            message.DeadLetterErrorDescription,
            failed ? "failed the story" : "the story was already settled"
        );
    }
}
