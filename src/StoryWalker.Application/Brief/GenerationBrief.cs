namespace StoryWalker.Application.Brief;

/// <summary>
/// The complete, schema-validated generation brief the agent emits when the interview is
/// confirmed (ADR-0013). The API validates it and enqueues it on the ingestion queue (ADR-0003);
/// the worker persists the pages and renders one image per page (ADR-0020).
/// This is the producer/consumer contract — serialized as camelCase JSON.
/// <para>
/// Every story is self-contained (ADR-0019): there is no character table and no link to another
/// story, so a character's appearance survives only inside the page prompts that describe it.
/// </para>
/// </summary>
/// <param name="TargetAge">
/// Age of the child the story is written for. Drives sentence length and vocabulary in the prose
/// the agent authors, and is appended to every illustration prompt by the worker.
/// </param>
/// <param name="ArtStyle">
/// One style phrase for the whole story. The worker appends it verbatim to every page prompt, and
/// that identical trailing text is what buys stylistic coherence across the six images.
/// </param>
public record GenerationBrief(
    string Title,
    int TargetAge,
    string ArtStyle,
    IReadOnlyList<BriefPage> Pages);

/// <param name="IllustrationPrompt">
/// Must restate, in full, the physical appearance of every character appearing on this page —
/// the model has no memory of the other pages' prompts.
/// </param>
public record BriefPage(
    int PageNo,
    string Text,
    string IllustrationPrompt);
