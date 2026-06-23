namespace StoryWalker.Contracts.Brief;

/// <summary>
/// The complete, schema-validated generation brief the agent emits when it has enough
/// information (ADR-0013). The API validates it and enqueues it on the ingestion queue
/// (ADR-0003); the Durable worker renders images and embeds the text from it (ADR-0004).
/// This is the producer/consumer contract — serialized as camelCase JSON.
/// </summary>
public record GenerationBrief(
    BriefStory Story,
    string ArtStyle,
    string Palette,
    IReadOnlyList<BriefCharacter> Characters,
    IReadOnlyList<BriefPage> Pages);

/// <param name="Title">The story title.</param>
/// <param name="BasedOnStoryId">Set when reusing another story's characters + art style; null for a fresh story.</param>
public record BriefStory(
    string Title,
    Guid? BasedOnStoryId);

/// <param name="ExistingCharacterId">
/// Set for a reused character so the worker's <c>ResolveRefImage</c> retrieves the existing
/// reference image instead of generating a new one; null for a new character.
/// </param>
public record BriefCharacter(
    string Name,
    string Description,
    string IllustrationPrompt,
    Guid? ExistingCharacterId);

/// <param name="Characters">Names of the characters who appear on this page; populates CHARACTER_PAGE.</param>
public record BriefPage(
    int PageNo,
    string Text,
    string IllustrationPrompt,
    IReadOnlyList<string> Characters);
