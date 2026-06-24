namespace StoryWalker.Application.Messaging;

/// <summary>
/// Output of the <c>PersistBrief</c> activity and the orchestrator's working state: the
/// persisted ids the downstream fan-out activities need (ADR-0004). Passed between the Durable
/// orchestrator and its activities, so it carries ids only — not domain entities.
/// </summary>
public record StoryPlan(
    Guid StoryId,
    IReadOnlyList<StoryPlanCharacter> Characters,
    IReadOnlyList<StoryPlanPage> Pages);

/// <summary>
/// Input to the <c>ResolveRefImage</c> activity: generate a reference image from
/// <see cref="IllustrationPrompt"/> for a new character, or retrieve the existing one when
/// <see cref="ExistingCharacterId"/> is set (ADR-0004/0005).
/// </summary>
public record StoryPlanCharacter(
    Guid StoryId,
    Guid CharacterId,
    string IllustrationPrompt,
    Guid? ExistingCharacterId);

/// <summary>
/// Input to the <c>GeneratePageImage</c> activity: render the page image from
/// <see cref="IllustrationPrompt"/>, conditioned on the reference images of
/// <see cref="CharacterIds"/> (ADR-0004).
/// </summary>
public record StoryPlanPage(
    Guid StoryId,
    Guid PageId,
    int PageNo,
    string IllustrationPrompt,
    IReadOnlyList<Guid> CharacterIds);
