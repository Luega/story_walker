using Microsoft.EntityFrameworkCore;
using StoryWalker.Api.Auth;
using StoryWalker.Application.Api;
using StoryWalker.Infrastructure.Persistence;
using StoryWalker.Infrastructure.Quota;

namespace StoryWalker.Api.Endpoints;

/// <summary>
/// Endpoints for the signed-in user's own record: the standing story context they save once and
/// the agent reads at the start of every new conversation, and where they stand against the weekly
/// story limit (ADR-0030).
/// <para>
/// Nothing here filters by owner, because there is no id to filter on: <see cref="CurrentUser"/>
/// <em>is</em> the scope, and it resolves the row from the token's subject (provisioning it on
/// first sight). The route is <c>/me</c> for the same reason.
/// </para>
/// </summary>
public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var me = app.MapGroup("/me").WithTags("Me").RequireAuthorization();

        me.MapGet(
                "/settings",
                async (StoryWalkerDbContext db, CurrentUser currentUser, CancellationToken ct) =>
                {
                    var userId = await currentUser.GetIdAsync(ct);

                    // A user who has never opened the settings page reads four nulls, which is what
                    // the form renders as empty boxes.
                    var settings = await db
                        .Users.Where(u => u.Id == userId)
                        .Select(u => new UserSettingsDto(
                            u.KidsContext,
                            u.StoryContext,
                            u.CharactersContext,
                            u.ArtStyleContext
                        ))
                        .FirstAsync(ct);

                    return Results.Ok(settings);
                }
            )
            .WithName("GetUserSettings");

        me.MapGet(
                "/quota",
                async (StoryQuotaService quota, CurrentUser currentUser, CancellationToken ct) =>
                    Results.Ok(await quota.GetAsync(await currentUser.GetIdAsync(ct), ct))
            )
            .WithName("GetQuota");

        me.MapPatch(
                "/settings",
                async (
                    UserSettingsDto request,
                    StoryWalkerDbContext db,
                    CurrentUser currentUser,
                    CancellationToken ct
                ) =>
                {
                    var errors = Validate(request);
                    if (errors.Count > 0)
                    {
                        return Results.ValidationProblem(errors);
                    }

                    var userId = await currentUser.GetIdAsync(ct);
                    var user = await db.Users.FirstAsync(u => u.Id == userId, ct);

                    // A full replace of the four, not a merge: the settings form always submits
                    // every field, so an answer the user cleared arrives as blank and must clear.
                    user.KidsContext = Normalise(request.Kids);
                    user.StoryContext = Normalise(request.Story);
                    user.CharactersContext = Normalise(request.Characters);
                    user.ArtStyleContext = Normalise(request.ArtStyle);

                    await db.SaveChangesAsync(ct);

                    // Echo what was stored rather than what was sent, so the form shows the trimmed
                    // truth — the same contract UpdateStoryTitle keeps.
                    return Results.Ok(
                        new UserSettingsDto(
                            user.KidsContext,
                            user.StoryContext,
                            user.CharactersContext,
                            user.ArtStyleContext
                        )
                    );
                }
            )
            .WithName("UpdateUserSettings");

        return app;
    }

    /// <summary>Blank — or whitespace only — is "not answered", which is null rather than "".</summary>
    private static string? Normalise(string? answer) =>
        string.IsNullOrWhiteSpace(answer) ? null : answer.Trim();

    /// <summary>
    /// The only rule is length, judged after trimming so trailing whitespace cannot fail an answer
    /// that fits. Keys are camelCase to match the JSON field the client sent.
    /// </summary>
    private static Dictionary<string, string[]> Validate(UserSettingsDto request)
    {
        var errors = new Dictionary<string, string[]>();

        Check("kids", request.Kids);
        Check("story", request.Story);
        Check("characters", request.Characters);
        Check("artStyle", request.ArtStyle);

        return errors;

        void Check(string field, string? answer)
        {
            if (Normalise(answer) is { } value && value.Length > UserSettingsDto.MaxFieldLength)
            {
                errors[field] =
                [
                    $"Keep this to {UserSettingsDto.MaxFieldLength} characters or fewer.",
                ];
            }
        }
    }
}
