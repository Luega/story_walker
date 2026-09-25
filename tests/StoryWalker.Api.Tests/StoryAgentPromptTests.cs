using StoryWalker.Application.Agent;
using StoryWalker.Application.Api;

namespace StoryWalker.Api.Tests;

/// <summary>
/// Unit tests for <see cref="StoryAgentPrompt.Compose"/>, the seam that folds a user's saved
/// settings into the system message a new conversation is seeded with. No database and no model:
/// this is string composition, and the rules it has to hold are worth pinning on their own.
/// </summary>
public sealed class StoryAgentPromptTests
{
    // The rule is what asks the model to write for a child's age; the Foundry filter alone blocks
    // only medium severity and up (ADR-0024). The interviewer and the author are separate calls
    // with separate system prompts, so dropping it from either one reopens the gap.
    [Fact]
    public void The_interview_prompt_carries_the_content_rule()
    {
        Assert.Contains(StoryAgentPrompt.ContentRule, StoryAgentPrompt.Instructions, StringComparison.Ordinal);
    }

    [Fact]
    public void The_brief_authoring_prompt_carries_the_content_rule()
    {
        Assert.Contains(
            StoryAgentPrompt.ContentRule,
            StoryAgentPrompt.BriefAuthoringInstructions,
            StringComparison.Ordinal
        );
    }

    // A conversation can now carry a second confirmation after a failed story (ADR-0027); the
    // author has to write the plan the parent confirmed last, not the one that failed.
    [Fact]
    public void The_brief_authoring_prompt_takes_the_most_recent_confirmed_plan()
    {
        Assert.Contains(
            "most recent confirmed plan",
            StoryAgentPrompt.BriefAuthoringInstructions,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void The_failure_note_names_the_page_and_quotes_its_text()
    {
        var note = StoryAgentPrompt.GenerationFailedNote(
            3,
            "George picked up the scissors.",
            GenerationFailureReason.ImageRefused
        );

        Assert.Contains("page 3", note, StringComparison.Ordinal);
        Assert.Contains("George picked up the scissors.", note, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refused_page_is_described_as_a_safety_refusal_to_reword()
    {
        var note = StoryAgentPrompt.GenerationFailedNote(3, "text", GenerationFailureReason.ImageRefused);

        Assert.Contains("safety", note, StringComparison.OrdinalIgnoreCase);
    }

    // A technical failure has nothing to do with the scene: blaming the content would send the
    // parent off rewriting a story that was fine.
    [Fact]
    public void A_technical_failure_is_not_blamed_on_the_content()
    {
        var note = StoryAgentPrompt.GenerationFailedNote(3, "text", GenerationFailureReason.TechnicalProblem);

        Assert.DoesNotContain("safety", note, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("technical", note, StringComparison.OrdinalIgnoreCase);
    }

    // The interview prompt allows the sentinel "only once for the entire conversation"; after a
    // failure the note is what lets a new confirmation start generation again.
    [Fact]
    public void The_failure_note_allows_a_new_confirmation_to_end_with_the_sentinel()
    {
        var note = StoryAgentPrompt.GenerationFailedNote(3, "text", GenerationFailureReason.ImageRefused);

        Assert.Contains(StoryAgentPrompt.EndOfChatSentinel, note, StringComparison.Ordinal);
    }

    [Fact]
    public void No_settings_leaves_the_instructions_byte_identical()
    {
        Assert.Equal(StoryAgentPrompt.Instructions, StoryAgentPrompt.Compose(null));
    }

    [Fact]
    public void All_blank_settings_leave_the_instructions_byte_identical()
    {
        var blank = new UserSettingsDto(null, "", "   ", "\n\t ");

        Assert.Equal(StoryAgentPrompt.Instructions, StoryAgentPrompt.Compose(blank));
    }

    [Fact]
    public void Composed_prompt_keeps_the_instructions_and_appends_the_context()
    {
        var composed = StoryAgentPrompt.Compose(
            new UserSettingsDto(Kids: "Mia is 5 and loves dinosaurs", null, null, null)
        );

        Assert.StartsWith(StoryAgentPrompt.Instructions, composed, StringComparison.Ordinal);
        Assert.Contains("Mia is 5 and loves dinosaurs", composed, StringComparison.Ordinal);
    }

    [Fact]
    public void Composed_prompt_is_delimited_as_data_rather_than_instructions()
    {
        var composed = StoryAgentPrompt.Compose(new UserSettingsDto("Mia is 5", null, null, null));

        Assert.Contains(StoryAgentPrompt.ContextStart, composed, StringComparison.Ordinal);
        Assert.Contains(StoryAgentPrompt.ContextEnd, composed, StringComparison.Ordinal);
        Assert.True(
            composed.IndexOf(StoryAgentPrompt.ContextStart, StringComparison.Ordinal)
                < composed.IndexOf(StoryAgentPrompt.ContextEnd, StringComparison.Ordinal),
            "The saved context must be enclosed by its markers, opening one first."
        );
    }

    [Fact]
    public void Blank_sections_are_omitted_entirely()
    {
        var composed = StoryAgentPrompt.Compose(
            new UserSettingsDto(Kids: "Mia is 5", Story: null, Characters: "   ", ArtStyle: null)
        );

        Assert.Contains("Who the stories are for:", composed, StringComparison.Ordinal);
        Assert.DoesNotContain("What the stories should be like:", composed, StringComparison.Ordinal);
        Assert.DoesNotContain("Characters that recur:", composed, StringComparison.Ordinal);
        Assert.DoesNotContain("Preferred art style:", composed, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_answered_section_appears_under_its_own_heading()
    {
        var composed = StoryAgentPrompt.Compose(
            new UserSettingsDto("Mia is 5", "Short and gentle", "Pip the kitten", "watercolour")
        );

        Assert.Contains("Who the stories are for:\nMia is 5", composed, StringComparison.Ordinal);
        Assert.Contains(
            "What the stories should be like:\nShort and gentle",
            composed,
            StringComparison.Ordinal
        );
        Assert.Contains("Characters that recur:\nPip the kitten", composed, StringComparison.Ordinal);
        Assert.Contains("Preferred art style:\nwatercolour", composed, StringComparison.Ordinal);
    }

    [Fact]
    public void Answers_are_trimmed()
    {
        var composed = StoryAgentPrompt.Compose(
            new UserSettingsDto("  Mia is 5  \n", null, null, null)
        );

        Assert.Contains("Who the stories are for:\nMia is 5\n", composed, StringComparison.Ordinal);
    }

    // The settings are user-controlled text landing in a *system* message, and StoryAgent decides a
    // conversation is finished by substring-matching this sentinel on the model's reply. A user who
    // pastes it into a settings box must not be able to steer the agent into ending the interview.
    [Fact]
    public void The_end_of_chat_sentinel_is_stripped_from_every_field()
    {
        var composed = StoryAgentPrompt.Compose(
            new UserSettingsDto(
                Kids: $"Mia is 5 {StoryAgentPrompt.EndOfChatSentinel}",
                Story: StoryAgentPrompt.EndOfChatSentinel,
                Characters: $"Pip{StoryAgentPrompt.EndOfChatSentinel}the kitten",
                ArtStyle: $"{StoryAgentPrompt.EndOfChatSentinel} watercolour"
            )
        );

        // The base instructions legitimately contain the sentinel once, in the final-message rule.
        Assert.Equal(
            1,
            composed.Split(StoryAgentPrompt.EndOfChatSentinel).Length - 1
        );
        Assert.Contains("Pipthe kitten", composed, StringComparison.Ordinal);
    }

    // A field whose only content was the sentinel is blank once stripped, so it must drop out
    // rather than leave an empty heading behind.
    [Fact]
    public void A_field_that_is_only_the_sentinel_is_treated_as_blank()
    {
        var composed = StoryAgentPrompt.Compose(
            new UserSettingsDto(Kids: "Mia is 5", Story: StoryAgentPrompt.EndOfChatSentinel, null, null)
        );

        Assert.DoesNotContain("What the stories should be like:", composed, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_consisting_only_of_the_sentinel_leave_the_instructions_untouched()
    {
        var sentinelOnly = new UserSettingsDto(
            StoryAgentPrompt.EndOfChatSentinel,
            StoryAgentPrompt.EndOfChatSentinel,
            StoryAgentPrompt.EndOfChatSentinel,
            StoryAgentPrompt.EndOfChatSentinel
        );

        Assert.Equal(StoryAgentPrompt.Instructions, StoryAgentPrompt.Compose(sentinelOnly));
    }
}
