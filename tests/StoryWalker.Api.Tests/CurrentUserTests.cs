using System.Data.Common;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using StoryWalker.Api.Auth;
using StoryWalker.Application.Api;
using StoryWalker.Domain.Entities;
using StoryWalker.Infrastructure.Persistence;

namespace StoryWalker.Api.Tests;

/// <summary>
/// Covers just-in-time provisioning through real requests, since a scoped service is only ever
/// reached that way. The conversations endpoint is the driver: at this point it is the only one
/// that resolves the caller (the story reads are scoped in the next task).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CurrentUserTests(StoryWalkerApiFactory factory)
{
    private readonly StoryWalkerApiFactory _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AgentMessageRequest Turn(string message) => new(ConversationId: null, message);

    [Fact]
    public async Task First_authenticated_request_creates_the_user()
    {
        await _factory.ResetAsync();
        var client = _factory.CreateClientAs("user_new", "new@storywalker.app");

        await client.PostAsJsonAsync("/stories/conversations", Turn("ciao"), Ct);

        var user = await _factory.ExecuteDbAsync(db =>
            db.Users.SingleAsync(u => u.ClerkUserId == "user_new", Ct)
        );
        Assert.Equal("new@storywalker.app", user.Email);
    }

    [Fact]
    public async Task Second_request_reuses_the_same_user()
    {
        await _factory.ResetAsync();
        var client = _factory.CreateClientAs("user_repeat");

        await client.PostAsJsonAsync("/stories/conversations", Turn("ciao"), Ct);
        await client.PostAsJsonAsync("/stories/conversations", Turn("ancora"), Ct);

        var count = await _factory.ExecuteDbAsync(db =>
            db.Users.CountAsync(u => u.ClerkUserId == "user_repeat", Ct)
        );
        Assert.Equal(1, count);
    }

    /// <summary>
    /// Forces (rather than hopes for) the race CurrentUser's catch block exists to handle. Two
    /// DbContexts share one ClerkUserId:
    /// <list type="number">
    /// <item>"B" inserts its row inside an explicit, uncommitted transaction. Postgres's MVCC
    /// guarantees no other connection can see it yet — not a timing assumption, a visibility
    /// rule.</item>
    /// <item>"A" is the real <see cref="CurrentUser"/> under test. Its GetIdAsync is started, and
    /// a <see cref="DbCommandInterceptor"/> on A's own DbContext signals the instant A's
    /// existing-user lookup has executed. Only once that signal fires — proof the lookup already
    /// ran while B was still uncommitted, so it is guaranteed to have found nothing — do we commit
    /// B. Without the interceptor, nothing stops the two HTTP-driven requests (or two bare
    /// GetIdAsync calls) from happening to serialise instead of overlap, which is exactly the
    /// scenario that let a broken catch block pass silently.</item>
    /// <item>Once B commits, A's own insert of the same ClerkUserId — already dispatched, or
    /// about to be — collides with it for real: either it was already blocked waiting on B's
    /// open transaction (Postgres holds a second inserter for a conflicting uncommitted key), or
    /// it lands after B is visible and fails immediately. Either way CurrentUser's catch block
    /// runs for real, not by chance.</item>
    /// </list>
    /// </summary>
    [Fact]
    public async Task Concurrent_first_requests_create_one_user()
    {
        await _factory.ResetAsync();

        const string subject = "user_race";
        var accessor = new StaticHttpContextAccessor(BuildPrincipal(subject, "a@storywalker.app"));

        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? throw new InvalidOperationException(
                "Expected the factory to have set the test Postgres connection string."
            );
        var selectSignal = new SelectCompletedInterceptor();
        var optionsA = new DbContextOptionsBuilder<StoryWalkerDbContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(selectSignal)
            .Options;
        await using var dbA = new StoryWalkerDbContext(optionsA);
        var currentUserA = new CurrentUser(accessor, dbA);

        using var scopeB = _factory.Services.CreateScope();
        var dbB = scopeB.ServiceProvider.GetRequiredService<StoryWalkerDbContext>();

        await using var txnB = await dbB.Database.BeginTransactionAsync(Ct);
        var winnerId = Guid.NewGuid();
        dbB.Users.Add(
            new User
            {
                Id = winnerId,
                ClerkUserId = subject,
                Email = "b@storywalker.app",
            }
        );
        await dbB.SaveChangesAsync(Ct); // Inserted but uncommitted — invisible to A's lookup.

        var idATask = currentUserA.GetIdAsync(Ct);
        await selectSignal.LookupCompleted; // A's existing-user SELECT has run and found nothing.
        await txnB.CommitAsync(Ct); // Only now does A's insert have anything to collide with.

        var idA = await idATask;

        Assert.Equal(winnerId, idA);
        var count = await _factory.ExecuteDbAsync(db =>
            db.Users.CountAsync(u => u.ClerkUserId == subject, Ct)
        );
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task New_story_is_owned_by_the_authenticated_user()
    {
        await _factory.ResetAsync();
        var client = _factory.CreateClientAs("user_owner");

        var response = await client.PostAsJsonAsync(
            "/stories/conversations",
            Turn(FakeStoryAgent.ConfirmMessage),
            Ct
        );

        response.EnsureSuccessStatusCode();
        var owner = await _factory.ExecuteDbAsync(db =>
            db.Stories.Select(s => s.User.ClerkUserId).SingleAsync(Ct)
        );
        Assert.Equal("user_owner", owner);
    }

    /// <summary>A Clerk-shaped principal carrying just the two claims <see cref="CurrentUser"/> reads.</summary>
    private static ClaimsPrincipal BuildPrincipal(string clerkUserId, string email) =>
        new(
            new ClaimsIdentity(
                [new Claim("sub", clerkUserId), new Claim("email", email)],
                authenticationType: "Test"
            )
        );

    /// <summary>
    /// Fixed-context <see cref="IHttpContextAccessor"/> for constructing a <see cref="CurrentUser"/>
    /// outside a real request, so the concurrency test can drive it directly instead of over HTTP.
    /// </summary>
    private sealed class StaticHttpContextAccessor(ClaimsPrincipal user) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = new DefaultHttpContext { User = user };
    }

    /// <summary>
    /// Signals the instant a query has come back from Postgres, so the concurrency test can prove
    /// its ordering instead of assuming it. <see cref="CurrentUser"/> issues exactly one read
    /// (the existing-user lookup) before it ever attempts to write, so the first completed reader
    /// is that lookup.
    /// </summary>
    private sealed class SelectCompletedInterceptor : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _lookupCompleted = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public Task LookupCompleted => _lookupCompleted.Task;

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default
        )
        {
            _lookupCompleted.TrySetResult();
            return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }
    }
}
