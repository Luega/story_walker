namespace StoryWalker.Domain.Enums;

/// <summary>
/// Per-page generation status. Written by the <c>GeneratePageImage</c> activity so the
/// dashboard has a source of truth independent of the Durable runtime (ER diagram; ADR-0005).
/// </summary>
public enum PageStatus
{
    Pending,
    Complete,
    Failed,
}
