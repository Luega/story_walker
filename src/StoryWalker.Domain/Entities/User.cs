using StoryWalker.Domain.Enums;

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

    /// <summary>
    /// Subscription plan. Defaulted rather than <c>required</c> so every caller that
    /// provisions a user — chiefly first sight of a Clerk subject — gets the free plan
    /// without having to say so.
    /// </summary>
    public UserTier Tier { get; set; } = UserTier.Free;

    public ICollection<Story> Stories { get; set; } = [];
}
