namespace StoryWalker.Domain.Entities;

/// <summary>
/// An authenticated owner of stories and characters. Identity comes from Clerk (ADR-0002):
/// <see cref="ClerkUserId"/> is the external subject and is unique.
/// </summary>
public class User
{
    public Guid Id { get; set; }

    /// <summary>Identity from Clerk (ADR-0002). Unique.</summary>
    public required string ClerkUserId { get; set; }

    public required string Email { get; set; }

    public ICollection<Story> Stories { get; set; } = new List<Story>();

    /// <summary>Characters are user-owned and reusable across the user's stories.</summary>
    public ICollection<Character> Characters { get; set; } = new List<Character>();
}
