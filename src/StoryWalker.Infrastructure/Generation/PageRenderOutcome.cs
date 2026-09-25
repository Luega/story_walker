namespace StoryWalker.Infrastructure.Generation;

/// <summary>
/// How rendering one page ended. The two failures are told apart because they call for different
/// advice when the story's failure is reported back to its conversation (ADR-0027): a refused scene
/// can be reworded, a technical failure can only be retried.
/// </summary>
public enum PageRenderOutcome
{
    /// <summary>The page has its image, now or from an earlier run.</summary>
    Rendered,

    /// <summary>
    /// The image model's safety system refused the page, and the one rewrite of its illustration
    /// prompt did not get past it either, or no usable rewrite came back (ADR-0025).
    /// </summary>
    Refused,

    /// <summary>Anything else: a wrong route or key, an empty response, a missing page.</summary>
    Failed,
}
