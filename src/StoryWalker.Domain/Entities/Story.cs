using StoryWalker.Domain.Enums;

namespace StoryWalker.Domain.Entities;

/// <summary>
/// A generated story. May <b>reuse</b> the characters and art style of another story via
/// <see cref="BasedOnStoryId"/> (ER diagram; ADR-0009). Brief-sourced fields
/// (<see cref="ArtStyle"/>, <see cref="Palette"/>, <see cref="Summary"/>) come from the
/// agent-authored generation brief (ADR-0013).
/// </summary>
public class Story
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>Nullable; the story whose characters + art style this one reuses (ADR-0009).</summary>
    public Guid? BasedOnStoryId { get; set; }

    public required string Title { get; set; }

    /// <summary>From the brief; also the source for the story-summary embedding.</summary>
    public required string Summary { get; set; }

    public StoryStatus Status { get; set; }

    /// <summary>From the brief; copied from the source story when reusing.</summary>
    public required string ArtStyle { get; set; }

    /// <summary>From the brief.</summary>
    public required string Palette { get; set; }

    /// <summary>
    /// Story-summary vector for semantic discovery (ADR-0009). Placeholder type: written by the
    /// <c>EmbedStory</c> activity. TODO: swap to <c>Pgvector.Vector</c> + HNSW index when the
    /// pgvector model/migrations land.
    /// </summary>
    public float[]? Embedding { get; set; }

    public DateTime CreatedAt { get; set; }

    public User User { get; set; } = null!;

    /// <summary>The story this one reuses characters/art style from (self-reference; nullable).</summary>
    public Story? BasedOnStory { get; set; }

    public ICollection<Page> Pages { get; set; } = new List<Page>();
}
