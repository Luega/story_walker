using StoryWalker.Domain.Enums;

namespace StoryWalker.Domain.Entities;

/// <summary>
/// One page of a story. Text and the illustration prompt come from the brief (ADR-0013);
/// <see cref="ImageUrl"/> is filled in by the worker's <c>PageImageRenderer</c> (null until done).
/// </summary>
public class Page : EntityBase
{
    public Guid StoryId { get; set; }

    public int PageNo { get; set; }

    /// <summary>Page text, from the brief (ADR-0013).</summary>
    public required string Content { get; set; }

    /// <summary>From the brief; generates the page image (ADR-0013).</summary>
    public required string IllustrationPrompt { get; set; }

    /// <summary>Blob URL; null until the image is generated (ADR-0010).</summary>
    public string? ImageUrl { get; set; }

    public PageStatus Status { get; set; }

    public Story Story { get; set; } = null!;
}
