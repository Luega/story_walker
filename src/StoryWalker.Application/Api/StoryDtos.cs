namespace StoryWalker.Application.Api;

// Inferred REST DTOs for the dashboard / story-viewer / agent flow. No endpoint contract is
// documented yet (C4 container diagram only) — refine when the API endpoints are designed.
// Status fields are strings to keep Contracts free of a Domain dependency (wire-friendly).

/// <summary>Dashboard list item — a story summary card.</summary>
public record StorySummaryDto(
    Guid Id,
    string Title,
    string Status,
    DateTime CreatedAt);

/// <summary>Full story for the live viewer.</summary>
public record StoryDto(
    Guid Id,
    string Title,
    string Summary,
    string Status,
    string ArtStyle,
    string Palette,
    Guid? BasedOnStoryId,
    IReadOnlyList<PageDto> Pages,
    IReadOnlyList<CharacterDto> Characters);

public record PageDto(
    int PageNo,
    string Content,
    string? ImageUrl,
    string Status);

public record CharacterDto(
    Guid Id,
    string Name,
    string Description,
    string? ReferenceImageUrl);

/// <summary>Body of the <c>202 Accepted</c> returned when generation is enqueued (ADR-0003).</summary>
public record CreateStoryResponse(
    Guid StoryId,
    string Status);
