namespace StoryWalker.Api.Tests;

/// <summary>A clock stopped at a chosen instant, so week boundaries can be tested (ADR-0030).</summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
