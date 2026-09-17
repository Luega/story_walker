using Microsoft.EntityFrameworkCore;
using StoryWalker.Domain.Entities;
using StoryWalker.Infrastructure.Persistence;

namespace StoryWalker.Api.Auth;

/// <summary>
/// Resolves the authenticated Clerk subject to the local <see cref="User"/> row, creating it on
/// first sight (ADR-0002). Identity lives in Clerk; this row is the mirror our foreign keys point
/// at. Scoped, and memoised per request so several endpoints in one request share one lookup.
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor accessor, StoryWalkerDbContext db)
{
    private Guid? _cached;

    public async Task<Guid> GetIdAsync(CancellationToken ct = default)
    {
        if (_cached is { } cached)
        {
            return cached;
        }

        var principal =
            accessor.HttpContext?.User
            ?? throw new InvalidOperationException(
                "CurrentUser was resolved outside a request; it is scoped to one."
            );

        var clerkUserId =
            principal.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("The token carries no 'sub' claim.");

        var existing = await db
            .Users.Where(u => u.ClerkUserId == clerkUserId)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct);

        if (existing is { } found)
        {
            _cached = found;
            return found;
        }

        // Only needed when provisioning: a returning user's email is already stored. Missing it
        // means the Clerk session token was never customised, so say exactly that.
        var email =
            principal.FindFirst("email")?.Value
            ?? throw new InvalidOperationException(
                "The token carries no 'email' claim. Add \"email\": \"{{user.primary_email_address}}\" "
                    + "to the session token in the Clerk dashboard (Sessions → customize session token)."
            );

        var user = new User
        {
            Id = Guid.NewGuid(),
            ClerkUserId = clerkUserId,
            Email = email,
        };
        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two first requests raced. The unique index on ClerkUserId picked a winner; read it
            // and carry on, rather than failing a request that did nothing wrong.
            db.Entry(user).State = EntityState.Detached;
            var winner = await db
                .Users.Where(u => u.ClerkUserId == clerkUserId)
                .Select(u => u.Id)
                .FirstAsync(ct);

            _cached = winner;
            return winner;
        }

        _cached = user.Id;
        return user.Id;
    }
}
