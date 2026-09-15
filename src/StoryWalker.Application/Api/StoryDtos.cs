namespace StoryWalker.Application.Api;

// REST DTOs for the story-viewer / agent flow. Status fields are strings to keep the wire
// contract free of a Domain dependency.

/// <summary>List item — a story summary card.</summary>
public record StorySummaryDto(Guid Id, string Title, string Status, DateTime CreatedAt);

/// <summary>Full story for the viewer.</summary>
public record StoryDto(
    Guid Id,
    string Title,
    int TargetAge,
    string Status,
    string ArtStyle,
    IReadOnlyList<PageDto> Pages
);

/// <summary><c>ImageUrl</c> is null until the worker has rendered this page (ADR-0010).</summary>
public record PageDto(int PageNo, string Content, string? ImageUrl, string Status);

/// <summary>Body of <c>PATCH /stories/{id}</c> — renames a story (title only).</summary>
public record UpdateStoryTitleRequest(string Title);
