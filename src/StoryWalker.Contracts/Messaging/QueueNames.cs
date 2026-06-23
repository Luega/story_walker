namespace StoryWalker.Contracts.Messaging;

/// <summary>
/// Azure Service Bus entity names shared by producer (<c>apps/api</c>) and consumer
/// (<c>apps/functions</c>). A single ingestion queue carries the generation brief and starts
/// the Durable orchestration (ADR-0003/0004).
/// </summary>
public static class QueueNames
{
    public const string Ingestion = "story-generation";
}
