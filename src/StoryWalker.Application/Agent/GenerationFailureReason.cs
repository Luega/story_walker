namespace StoryWalker.Application.Agent;

/// <summary>
/// Why a story could not be illustrated, as far as the agent needs to know to advise the parent
/// (ADR-0027). Deliberately coarse: the agent is told the category, never the provider's error.
/// </summary>
public enum GenerationFailureReason
{
    /// <summary>The image safety system refused a page, even after its illustration was reworded.</summary>
    ImageRefused,

    /// <summary>Anything else that stopped the illustrations.</summary>
    TechnicalProblem,
}
