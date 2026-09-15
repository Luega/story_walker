using Azure.Messaging.ServiceBus;
using StoryWalker.Application.Brief;
using StoryWalker.Application.Messaging;

namespace StoryWalker.Infrastructure.Messaging;

/// <summary>
/// Publishes the validated brief onto the Service Bus ingestion queue (ADR-0003). The body is the
/// camelCase brief JSON (ADR-0013); the <c>Story</c> id becomes the message id, both correlating the
/// message to its persisted story row and enabling at-least-once dedup on the consumer (ADR-0005).
/// The <see cref="ServiceBusSender"/> is cached for the lifetime of the (singleton) publisher.
/// </summary>
internal sealed class ServiceBusBriefPublisher(ServiceBusClient client) : IBriefPublisher
{
    private readonly ServiceBusSender _sender = client.CreateSender(QueueNames.Ingestion);

    public async Task PublishAsync(Guid storyId, GenerationBrief brief, CancellationToken ct = default)
    {
        var message = new ServiceBusMessage(BriefSerialization.Serialize(brief))
        {
            MessageId = storyId.ToString(),
            ContentType = "application/json",
        };

        await _sender.SendMessageAsync(message, ct);
    }
}
