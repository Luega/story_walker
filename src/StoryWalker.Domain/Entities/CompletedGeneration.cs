namespace StoryWalker.Domain.Entities;

/// <summary>
/// One finished story, as the weekly limit counts it (ADR-0030). Appended by the worker when a
/// story settles <c>Complete</c>, and never removed by the user: deleting the story only sets
/// <see cref="StoryId"/> to null, so deleting and regenerating cannot get around the limit.
/// <para>
/// Not an <see cref="EntityBase"/>: <see cref="CompletedAt"/> is set explicitly (the migration
/// backfills it for older stories), not stamped by SaveChanges.
/// </para>
/// </summary>
public class CompletedGeneration
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>The story that finished; null once it has been deleted.</summary>
    public Guid? StoryId { get; set; }

    public DateTime CompletedAt { get; set; }
}
