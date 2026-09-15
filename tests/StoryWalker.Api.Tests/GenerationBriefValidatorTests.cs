using StoryWalker.Application.Brief;

namespace StoryWalker.Api.Tests;

/// <summary>
/// Pure unit tests for <see cref="GenerationBriefValidator"/> — no DB, no HTTP. A known-good brief
/// passes; each crafted defect produces at least one error.
/// </summary>
public sealed class GenerationBriefValidatorTests
{
    private static GenerationBrief ValidBrief() =>
        new(
            Title: "Luna and the Rainy Day",
            TargetAge: 5,
            ArtStyle: "watercolour, soft pastels",
            Pages: [.. PagesNumbered(GenerationBriefValidator.PageCount)]
        );

    private static IEnumerable<BriefPage> PagesNumbered(int count) =>
        Enumerable
            .Range(1, count)
            .Select(n => new BriefPage(n, $"Luna woke to the rain. ({n})", $"Luna at a rainy window ({n})"));

    [Fact]
    public void Valid_brief_has_no_errors()
    {
        Assert.Empty(GenerationBriefValidator.Validate(ValidBrief()));
    }

    [Fact]
    public void Blank_title_or_art_style_is_an_error()
    {
        var brief = ValidBrief() with { Title = "  ", ArtStyle = "" };

        Assert.Equal(2, GenerationBriefValidator.Validate(brief).Count);
    }

    [Fact]
    public void No_pages_is_an_error()
    {
        var brief = ValidBrief() with { Pages = [] };
        Assert.NotEmpty(GenerationBriefValidator.Validate(brief));
    }

    [Fact]
    public void Fewer_than_six_pages_is_an_error()
    {
        var brief = ValidBrief() with { Pages = [.. PagesNumbered(5)] };

        Assert.Contains(GenerationBriefValidator.Validate(brief), e => e.Contains("exactly 6 pages"));
    }

    [Fact]
    public void More_than_six_pages_is_an_error()
    {
        var brief = ValidBrief() with { Pages = [.. PagesNumbered(7)] };

        Assert.Contains(GenerationBriefValidator.Validate(brief), e => e.Contains("exactly 6 pages"));
    }

    [Fact]
    public void Target_age_below_the_minimum_is_an_error()
    {
        var brief = ValidBrief() with { TargetAge = 0 };

        Assert.Contains(GenerationBriefValidator.Validate(brief), e => e.Contains("Target age"));
    }

    [Fact]
    public void Target_age_above_the_maximum_is_an_error()
    {
        var brief = ValidBrief() with { TargetAge = GenerationBriefValidator.MaxTargetAge + 1 };

        Assert.Contains(GenerationBriefValidator.Validate(brief), e => e.Contains("Target age"));
    }

    [Fact]
    public void Non_contiguous_page_numbers_are_an_error()
    {
        var pages = PagesNumbered(GenerationBriefValidator.PageCount).ToList();
        pages[5] = pages[5] with { PageNo = 99 };
        var brief = ValidBrief() with { Pages = pages };

        Assert.Contains(GenerationBriefValidator.Validate(brief), e => e.Contains("contiguously"));
    }

    [Fact]
    public void Page_missing_text_or_prompt_is_an_error()
    {
        var pages = PagesNumbered(GenerationBriefValidator.PageCount).ToList();
        pages[0] = pages[0] with { Text = "", IllustrationPrompt = "  " };
        var brief = ValidBrief() with { Pages = pages };

        Assert.Equal(2, GenerationBriefValidator.Validate(brief).Count);
    }
}
