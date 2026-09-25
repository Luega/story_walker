namespace StoryWalker.Application.Agent;

/// <summary>
/// Thrown when a new conversation is asked to start from a story the user cannot build on: one
/// that does not exist, is someone else's, or is not <c>Complete</c> (ADR-0029). The endpoint maps
/// it to 404, as it does for a conversation that is not the caller's.
/// </summary>
public sealed class BasedOnStoryNotFoundException(Guid storyId)
    : Exception($"Story '{storyId}' cannot be built on.")
{
    public Guid StoryId { get; } = storyId;
}
