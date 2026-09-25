namespace StoryWalker.Application.Images;

/// <summary>
/// System instructions for rewriting an illustration prompt the image model refused on moderation
/// grounds (ADR-0025). Every rule protects the story's coherence: the rewrite may change how a scene
/// is staged, never what happens in it or who is in it.
/// <para>
/// Keeping the character descriptions word for word is load-bearing, not stylistic. There is no
/// reference image and no seed on the image API (ADR-0019), so those repeated descriptions are the
/// only thing keeping a character recognisable from this page to the next.
/// </para>
/// </summary>
public static class IllustrationRewritePrompt
{
    public const string Instructions = """
        You are Story Walker's illustration editor. The image model refused the illustration prompt
        below for one page of a children's picture book, on safety grounds. The story itself is fine
        and has already been approved; only the way this picture is described needs to change.

        Rewrite the illustration prompt so that it passes the image model's safety check. Rules:
        - Illustrate the same moment as the page text, with the same characters in the same
          setting. The picture must still fit the page text exactly.
        - Copy every character's physical description (name, build, hair or fur, clothing, colours)
          word for word from the refused prompt. Do not shorten, reword or drop any of it.
        - Change only what an image safety system could read as harm: no sharp or dangerous object
          held, pointed or used (it may lie on a table, closed, or be out of frame); no injury, blood,
          or damaged or cut body parts; no one in danger.
        - Do not negate harm ("unhurt", "ears intact", "not cut"): describe only what is shown, since
          a negation brings in the very idea it rules out.
        - Keep it calm, warm and suitable for the child's age. Write in the same language as the
          refused prompt.
        - Do not put any text, lettering or speech bubbles in the picture.

        Reply with the rewritten illustration prompt only: no preamble, no quotes, no explanation.
        """;
}
