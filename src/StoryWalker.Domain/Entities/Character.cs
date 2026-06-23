namespace StoryWalker.Domain.Entities;

/// <summary>
/// A user-owned character, reusable across the user's stories. <see cref="ReferenceImageUrl"/>
/// and <see cref="Seed"/> give the character its deterministic visual identity (ADR-0008);
/// reuse is always by linking this row, never by a similarity match (ADR-0009).
/// </summary>
public class Character
{
    public Guid Id { get; set; }

    /// <summary>User-owned; reusable across the user's stories.</summary>
    public Guid UserId { get; set; }

    public required string Name { get; set; }

    /// <summary>From the brief (ADR-0013); also the source for the description embedding.</summary>
    public required string Description { get; set; }

    /// <summary>From the brief; generates the reference image (ADR-0013).</summary>
    public required string IllustrationPrompt { get; set; }

    /// <summary>Blob URL of the reference image; null until generated (ADR-0008/0010).</summary>
    public string? ReferenceImageUrl { get; set; }

    /// <summary>Locked per character for deterministic identity (ADR-0008).</summary>
    public long Seed { get; set; }

    /// <summary>
    /// Description vector for semantic discovery (ADR-0009). Placeholder type: written by the
    /// <c>EmbedStory</c> activity. TODO: swap to <c>Pgvector.Vector</c> + HNSW index when the
    /// pgvector model/migrations land.
    /// </summary>
    public float[]? Embedding { get; set; }

    public User User { get; set; } = null!;

    /// <summary>The pages this character appears on (populates the image conditioning).</summary>
    public ICollection<CharacterPage> CharacterPages { get; set; } = new List<CharacterPage>();
}
