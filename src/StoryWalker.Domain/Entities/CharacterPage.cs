namespace StoryWalker.Domain.Entities;

/// <summary>
/// Join row recording that a <see cref="Character"/> appears on a <see cref="Page"/>, so the
/// image step conditions on exactly the right reference images (ER diagram; ADR-0008).
/// Composite key <c>(CharacterId, PageId)</c>.
/// </summary>
public class CharacterPage
{
    public Guid CharacterId { get; set; }

    public Guid PageId { get; set; }

    public Character Character { get; set; } = null!;

    public Page Page { get; set; } = null!;
}
