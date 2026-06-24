namespace StoryWalker.Domain.Entities;

/// <summary>
/// Base type for entities with their own identity. The join entity
/// <see cref="CharacterPage"/> does not inherit this — it has a composite key and no own id.
/// </summary>
public abstract class EntityBase
{
    public Guid Id { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Null until the row is first modified; set automatically on update.</summary>
    public DateTime? UpdatedAt { get; set; }
}
