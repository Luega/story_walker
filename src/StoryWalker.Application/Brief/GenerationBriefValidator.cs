namespace StoryWalker.Application.Brief;

/// <summary>
/// Validates a <see cref="GenerationBrief"/> the agent authored before it is trusted as the
/// producer/consumer contract (ADR-0013: "the agent's JSON must be validated"). Returns the list
/// of problems; an empty list means the brief is well-formed. Structural, not stylistic — it
/// guards the invariants the worker and the DB model rely on, not prose quality.
/// </summary>
public static class GenerationBriefValidator
{
    /// <summary>Hard cap on pages per story (product rule; keeps the brief and generation bounded).</summary>
    public const int MaxPages = 10;

    public static IReadOnlyList<string> Validate(GenerationBrief brief)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(brief.Story.Title))
        {
            errors.Add("Story title is required.");
        }

        if (string.IsNullOrWhiteSpace(brief.ArtStyle))
        {
            errors.Add("Art style is required.");
        }

        if (string.IsNullOrWhiteSpace(brief.Palette))
        {
            errors.Add("Palette is required.");
        }

        ValidateCharacters(brief, errors);
        ValidatePages(brief, errors);

        return errors;
    }

    private static void ValidateCharacters(GenerationBrief brief, List<string> errors)
    {
        if (brief.Characters.Count == 0)
        {
            errors.Add("At least one character is required.");
            return;
        }

        foreach (var character in brief.Characters)
        {
            if (string.IsNullOrWhiteSpace(character.Name))
            {
                errors.Add("A character is missing its name.");
            }

            if (string.IsNullOrWhiteSpace(character.Description))
            {
                errors.Add($"Character '{character.Name}' is missing a description.");
            }

            if (string.IsNullOrWhiteSpace(character.IllustrationPrompt))
            {
                errors.Add($"Character '{character.Name}' is missing an illustration prompt.");
            }
        }

        var duplicateNames = brief
            .Characters.Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        foreach (var name in duplicateNames)
        {
            errors.Add($"Duplicate character name '{name}'.");
        }
    }

    private static void ValidatePages(GenerationBrief brief, List<string> errors)
    {
        if (brief.Pages.Count == 0)
        {
            errors.Add("At least one page is required.");
            return;
        }

        if (brief.Pages.Count > MaxPages)
        {
            errors.Add($"A story may have at most {MaxPages} pages, but the brief has {brief.Pages.Count}.");
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

        var characterNames = brief
            .Characters.Select(c => c.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

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

            foreach (var name in page.Characters.Where(n => !characterNames.Contains(n)))
            {
                errors.Add($"Page {page.PageNo} references unknown character '{name}'.");
            }
        }
    }
}
