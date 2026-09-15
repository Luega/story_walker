namespace StoryWalker.Application.Brief;

/// <summary>
/// Validates a <see cref="GenerationBrief"/> the agent authored before it is trusted as the
/// producer/consumer contract (ADR-0013: "the agent's JSON must be validated"). Returns the list
/// of problems; an empty list means the brief is well-formed. Structural, not stylistic — it
/// guards the invariants the worker and the DB model rely on, not prose quality.
/// </summary>
public static class GenerationBriefValidator
{
    /// <summary>
    /// Every story is exactly this long (product rule, ADR-0019). An exact count rather than a cap:
    /// the worker sizes its per-page work from it, and the agent prompt promises the same number.
    /// </summary>
    public const int PageCount = 6;

    /// <summary>Bounds on the child's age. Keeps a typo from asking for prose for a 0-year-old.</summary>
    public const int MinTargetAge = 1;

    public const int MaxTargetAge = 17;

    public static IReadOnlyList<string> Validate(GenerationBrief brief)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(brief.Title))
        {
            errors.Add("Story title is required.");
        }

        if (string.IsNullOrWhiteSpace(brief.ArtStyle))
        {
            errors.Add("Art style is required.");
        }

        if (brief.TargetAge is < MinTargetAge or > MaxTargetAge)
        {
            errors.Add(
                $"Target age must be between {MinTargetAge} and {MaxTargetAge}, but the brief has {brief.TargetAge}."
            );
        }

        ValidatePages(brief, errors);

        return errors;
    }

    private static void ValidatePages(GenerationBrief brief, List<string> errors)
    {
        if (brief.Pages.Count != PageCount)
        {
            errors.Add($"A story must have exactly {PageCount} pages, but the brief has {brief.Pages.Count}.");
        }

        if (brief.Pages.Count == 0)
        {
            return;
        }

        // pageNo must be contiguous 1..N so the worker can order and persist pages deterministically.
        var ordered = brief.Pages.OrderBy(p => p.PageNo).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].PageNo != i + 1)
            {
                errors.Add("Page numbers must run contiguously from 1.");
                break;
            }
        }

        foreach (var page in brief.Pages)
        {
            if (string.IsNullOrWhiteSpace(page.Text))
            {
                errors.Add($"Page {page.PageNo} is missing its text.");
            }

            if (string.IsNullOrWhiteSpace(page.IllustrationPrompt))
            {
                errors.Add($"Page {page.PageNo} is missing an illustration prompt.");
            }
        }
    }
}
