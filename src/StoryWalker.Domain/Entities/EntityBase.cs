namespace StoryWalker.Domain.Entities;

/// <summary>
/// Base type for entities with their own identity. Timestamps are stamped by the DbContext's
/// SaveChanges override, never by callers.
/// </summary>
public abstract class EntityBase
{
    public Guid Id { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Null until the row is first modified; set automatically on update.</summary>
    public DateTime? UpdatedAt { get; set; }
}
