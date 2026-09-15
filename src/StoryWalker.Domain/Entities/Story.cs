using StoryWalker.Domain.Enums;

namespace StoryWalker.Domain.Entities;

/// <summary>
/// A generated story. Every story is self-contained: it owns its pages and shares nothing with
/// any other story (ADR-0019). Brief-sourced fields (<see cref="Title"/>, <see cref="TargetAge"/>,
/// <see cref="ArtStyle"/>) come from the agent-authored generation brief (ADR-0013).
/// </summary>
public class Story : EntityBase
{
    public Guid UserId { get; set; }

    public required string Title { get; set; }

    /// <summary>
    /// Age of the child the story was written for, from the brief (ADR-0013). Drives sentence
    /// length and vocabulary in the authored prose, and the illustration prompts.
    /// </summary>
    public int TargetAge { get; set; }

    public StoryStatus Status { get; set; }

    /// <summary>From the brief; appended to every page's illustration prompt by the worker.</summary>
    public required string ArtStyle { get; set; }

    public User User { get; set; } = null!;

    public ICollection<Page> Pages { get; set; } = [];
}
