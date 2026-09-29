namespace StoryWalker.Application.Messaging;

/// <summary>
/// Azure Service Bus entity names shared by producer (<c>apps/api</c>) and consumer
/// (<c>apps/functions</c>). A single ingestion queue carries the generation brief and triggers
/// the generation function (ADR-0003/0020).
/// </summary>
public static class QueueNames
{
    public const string Ingestion = "story-generation";

    /// <summary>
    /// Where Service Bus moves an ingestion message after ten failed deliveries, or once it expires.
    /// Read by the worker, which settles the story and removes the message (ADR-0032).
    /// </summary>
    public const string IngestionDeadLetter = Ingestion + "/$DeadLetterQueue";
}
