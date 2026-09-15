namespace StoryWalker.Domain.Entities;

/// <summary>
/// An authenticated owner of stories. Identity comes from Clerk (ADR-0002):
/// <see cref="ClerkUserId"/> is the external subject and is unique.
/// </summary>
public class User : EntityBase
{
    /// <summary>Identity from Clerk (ADR-0002). Unique.</summary>
    public required string ClerkUserId { get; set; }

    public required string Email { get; set; }

    public ICollection<Story> Stories { get; set; } = [];
}
