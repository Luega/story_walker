namespace StoryWalker.Domain.Enums;

/// <summary>
/// Lifecycle of a <see cref="Entities.Story"/>. Persisted as a string and surfaced on the
/// dashboard as the source of truth for generation progress (ER diagram; ADR-0004/0005).
/// </summary>
public enum StoryStatus
{
    Draft,
    Generating,
    Complete,
    Failed,
}
