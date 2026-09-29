namespace StoryWalker.Domain.Enums;

/// <summary>
/// Per-page generation status. Written by the worker's <c>PageImageRenderer</c>; it is also the
/// worker's checkpoint, since a redelivered message resumes at the first page not
/// <see cref="Complete"/> (ADR-0005/0020).
/// </summary>
public enum PageStatus
{
    Pending,
    Complete,
    Failed,
}
