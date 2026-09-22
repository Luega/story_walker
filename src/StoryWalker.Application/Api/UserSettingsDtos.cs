namespace StoryWalker.Application.Api;

/// <summary>
/// The four standing-context answers a user saves on the settings page, and which are composed
/// into the system prompt of every new conversation (<see cref="Agent.StoryAgentPrompt.Compose"/>).
/// <para>
/// One record serves as both the <c>GET /me/settings</c> response and the <c>PATCH /me/settings</c>
/// body: the settings form always submits all four fields, so a PATCH replaces the whole set and a
/// null or blank field clears that answer. It is PATCH rather than PUT only because the UI's
/// server-side token proxy forwards GET, POST, PATCH and DELETE, and no PUT.
/// </para>
/// <para>
/// Every field is free text — deliberately, rather than named children, an art-style picker or
/// themed checkboxes. The consumer is a language model reading prose, and a parent describing
/// their child in their own words tells it more than any set of fields we could have guessed at.
/// </para>
/// </summary>
public record UserSettingsDto(string? Kids, string? Story, string? Characters, string? ArtStyle)
{
    /// <summary>
    /// Cap on each answer, enforced by the endpoint and by the column. Generous enough for a real
    /// paragraph, small enough that four of them cannot crowd out the instructions they follow.
    /// </summary>
    public const int MaxFieldLength = 600;
}
