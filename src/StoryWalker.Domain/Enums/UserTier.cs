namespace StoryWalker.Domain.Enums;

/// <summary>
/// Subscription plan a <see cref="Entities.User"/> is on. Persisted as a string, like the other
/// enums in the model. Only <see cref="Free"/> exists today: paid plans will add values here, and
/// whatever they gate is decided then, not now.
/// </summary>
public enum UserTier
{
    Free,
}
