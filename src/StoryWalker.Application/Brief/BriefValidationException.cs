namespace StoryWalker.Application.Brief;

/// <summary>
/// Thrown when the agent-authored <see cref="GenerationBrief"/> still fails
/// <see cref="GenerationBriefValidator"/> after the repair retry (ADR-0013). The turn is rolled
/// back so nothing is persisted and the client can resend the confirmation to author again.
/// </summary>
public sealed class BriefValidationException(IReadOnlyList<string> errors)
    : Exception("The generation brief the agent produced was invalid: " + string.Join(" ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
