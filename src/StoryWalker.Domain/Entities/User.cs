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

    /// <summary>
    /// Standing context the user saves once and which is composed into the system prompt of every
    /// new agent conversation, so they do not retype their children's ages, the kind of stories
    /// they want, the recurring cast and the look they like at the start of every story.
    /// <para>
    /// Free text, capped at <c>UserSettingsDto.MaxFieldLength</c>. Nullable and defaulted rather
    /// than <c>required</c>, for the same reason <see cref="Tier"/> is: just-in-time provisioning
    /// on first sight of a Clerk subject should not have to know these exist. Null means "not
    /// answered", which is not the same as an empty answer — the endpoint normalises blank to null.
    /// </para>
    /// </summary>
    public string? KidsContext { get; set; }

    /// <inheritdoc cref="KidsContext"/>
    public string? StoryContext { get; set; }

    /// <inheritdoc cref="KidsContext"/>
    public string? CharactersContext { get; set; }

    /// <inheritdoc cref="KidsContext"/>
    public string? ArtStyleContext { get; set; }

    public ICollection<Story> Stories { get; set; } = [];
}
