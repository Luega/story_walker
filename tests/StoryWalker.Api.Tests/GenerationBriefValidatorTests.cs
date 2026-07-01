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
            new BriefStory("Luna and the Rainy Day", BasedOnStoryId: null),
            ArtStyle: "watercolour",
            Palette: "soft pastels",
            Characters:
            [
                new BriefCharacter("Luna", "round-faced girl", "Luna, a round-faced girl", null),
            ],
            Pages:
            [
                new BriefPage(1, "Luna woke to the rain.", "Luna at a rainy window", ["Luna"]),
                new BriefPage(2, "She went outside.", "Luna splashing", ["Luna"]),
            ]
        );

    [Fact]
    public void Valid_brief_has_no_errors()
    {
        Assert.Empty(GenerationBriefValidator.Validate(ValidBrief()));
    }

    [Fact]
    public void Blank_title_art_style_or_palette_is_an_error()
    {
        var brief = ValidBrief() with
        {
            Story = new BriefStory("  ", null),
            ArtStyle = "",
            Palette = "",
        };

        Assert.Equal(3, GenerationBriefValidator.Validate(brief).Count);
    }

    [Fact]
    public void No_characters_is_an_error()
    {
        var brief = ValidBrief() with { Characters = [] };
        Assert.NotEmpty(GenerationBriefValidator.Validate(brief));
    }

    [Fact]
    public void Duplicate_character_names_are_an_error()
    {
        var brief = ValidBrief() with
        {
            Characters =
            [
                new BriefCharacter("Luna", "a girl", "prompt", null),
                new BriefCharacter("luna", "another", "prompt", null),
            ],
        };

        Assert.Contains(GenerationBriefValidator.Validate(brief), e => e.Contains("Duplicate"));
    }

    [Fact]
    public void Character_missing_a_field_is_an_error()
    {
        var brief = ValidBrief() with { Characters = [new BriefCharacter("Luna", "", "  ", null)] };

        Assert.Equal(2, GenerationBriefValidator.Validate(brief).Count);
    }

    [Fact]
    public void No_pages_is_an_error()
    {
        var brief = ValidBrief() with { Pages = [] };
        Assert.NotEmpty(GenerationBriefValidator.Validate(brief));
    }

    [Fact]
    public void More_than_ten_pages_is_an_error()
    {
        var pages = Enumerable
            .Range(1, 11)
            .Select(n => new BriefPage(n, $"text {n}", $"prompt {n}", ["Luna"]))
            .ToList();
        var brief = ValidBrief() with { Pages = pages };

        Assert.Contains(GenerationBriefValidator.Validate(brief), e => e.Contains("at most"));
    }

    [Fact]
    public void Non_contiguous_page_numbers_are_an_error()
    {
        var brief = ValidBrief() with
        {
            Pages = [new BriefPage(1, "a", "p", ["Luna"]), new BriefPage(3, "b", "p", ["Luna"])],
        };

        Assert.Contains(GenerationBriefValidator.Validate(brief), e => e.Contains("contiguously"));
    }

    [Fact]
    public void Page_missing_text_or_prompt_is_an_error()
    {
        var brief = ValidBrief() with { Pages = [new BriefPage(1, "", "  ", ["Luna"])] };

        Assert.Equal(2, GenerationBriefValidator.Validate(brief).Count);
    }

    [Fact]
    public void Page_referencing_unknown_character_is_an_error()
    {
        var brief = ValidBrief() with { Pages = [new BriefPage(1, "text", "prompt", ["Nobody"])] };

        Assert.Contains(
            GenerationBriefValidator.Validate(brief),
            e => e.Contains("unknown character")
        );
    }
}
