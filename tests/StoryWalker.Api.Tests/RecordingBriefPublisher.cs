using StoryWalker.Application.Brief;
using StoryWalker.Application.Messaging;

namespace StoryWalker.Api.Tests;

/// <summary>
/// Test double for <see cref="IBriefPublisher"/> that records what the endpoint enqueued instead of
/// talking to Service Bus, so the "brief ready → 202" path can be asserted in isolation.
/// </summary>
public sealed class RecordingBriefPublisher : IBriefPublisher
{
    private readonly List<(Guid StoryId, GenerationBrief Brief)> _published = [];

    public IReadOnlyList<(Guid StoryId, GenerationBrief Brief)> Published => _published;

    public Task PublishAsync(Guid storyId, GenerationBrief brief, CancellationToken ct = default)
    {
        _published.Add((storyId, brief));
        return Task.CompletedTask;
    }
}
