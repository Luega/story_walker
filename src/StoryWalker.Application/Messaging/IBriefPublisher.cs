using StoryWalker.Application.Brief;

namespace StoryWalker.Application.Messaging;

/// <summary>
/// Outbound port that publishes a validated <see cref="GenerationBrief"/> onto the ingestion queue
/// (<see cref="QueueNames.Ingestion"/>) so the Durable Functions worker can start the generation
/// pipeline (ADR-0003/0004). The brief JSON is the message body (ADR-0013); <paramref name="storyId"/>
/// travels as the message id, correlating the queued work to the <c>Story</c> row the API persisted
/// at enqueue time and giving the consumer an idempotency key (ADR-0005).
/// </summary>
public interface IBriefPublisher
{
    Task PublishAsync(Guid storyId, GenerationBrief brief, CancellationToken ct = default);
}
