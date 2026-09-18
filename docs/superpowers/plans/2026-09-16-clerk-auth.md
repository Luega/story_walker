# Clerk Authentication Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every story and conversation belongs to the signed-in Clerk user, enforced by the API and reached through a server-side token proxy in Next.js.

**Architecture:** The browser holds only a Clerk session cookie and talks to a single origin. A Next.js Route Handler at `app/api/[...path]/route.ts` exchanges that session for a JWT and forwards it to the ASP.NET Core API as a Bearer token. The API validates the JWT with the standard `JwtBearer` middleware, provisions a local `User` row on first sight of a `sub`, and scopes every query by owner.

**Tech Stack:** Next.js 16.3.5 (App Router, `proxy.ts`), `@clerk/nextjs`, ASP.NET Core 10 minimal APIs, `Microsoft.AspNetCore.Authentication.JwtBearer`, EF Core 10 + PostgreSQL, xUnit v3 + Testcontainers, Vitest 5 (Chromium + node projects).

**Spec:** [`docs/superpowers/specs/2026-09-16-clerk-auth-design.md`](../specs/2026-09-16-clerk-auth-design.md)

## Global Constraints

- **NuGet versions are central (ADR-0016).** Never put `Version=` on a `PackageReference`; add a `PackageVersion` to `Directory.Packages.props` instead.
- **Layering (ADR-0015).** Auth is a web-layer concern: new C# code goes in `apps/api/Auth/`. `Domain`, `Application` and `Infrastructure` change only where this plan says so explicitly.
- **Clerk instance URL** is configured as `Clerk:Authority`; **authorized parties** as `Clerk:AuthorizedParties`, a comma-separated list. Blank counts as unconfigured.
- **`ValidateAudience` is always `false`** — Clerk session tokens carry no `aud` claim. The `azp` check replaces it and is not optional.
- **`MapInboundClaims = false`** on the JWT bearer options, so `sub`, `azp` and `email` are readable under their JWT names rather than the WS-Federation URIs .NET maps them to by default.
- **UI copy is in Italian.** Clerk components are localized with `itIT` from `@clerk/localizations`.
- **Tests run offline.** No task may introduce a test that reaches the network.
- **`ClockSkew` is 5 seconds**, never .NET's 5-minute default: Clerk session tokens live 60
  seconds and cannot be revoked once issued, so the skew is added onto the post-sign-out
  window.
- **Clerk Core 3 (`@clerk/nextjs` 7.x) overrides the UI snippets below** — found while executing
  Task 5 on 2026-09-18; the snippets were written against the pre-Core-3 API:
  - `<SignedIn>`, `<SignedOut>` and `<Protect>` are removed. They are still exported, so imports and
    types compile, but **rendering one throws**. Use `<Show when="signed-in">` /
    `<Show when="signed-out">`. Component tests never render the layout, so the suite cannot catch it.
  - `<ClerkProvider>` goes **inside `<body>`**, not wrapping `<html>`.
  - `auth().getToken()` **throws `ClerkOfflineError`** when Clerk is unreachable, instead of
    returning `null`. A `null` still means no session.

### Manual Clerk dashboard setup (do this before Task 5)

1. Create the Clerk application; copy the publishable key and the secret key.
2. **Sessions → customize session token**: add `"email": "{{user.primary_email_address}}"`. `User.Email` is `required`, and `email` is *not* a default claim — without this step provisioning throws at runtime.
3. Note the instance URL (e.g. `https://xxx-yyy-00.clerk.accounts.dev`) — that is `Clerk:Authority`.

### Known intermediate state

Task 1 makes the API reject unauthenticated requests, and the UI does not send a token until Task 6. Between those tasks the dashboard shows its error banner in a browser. Every test suite stays green throughout; this is expected, not a regression.

---

### Task 1: JWT bearer authentication on the API

The API rejects requests without a valid Clerk token. No per-user filtering yet — that is Tasks 2–4.

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `apps/api/StoryWalker.Api.csproj`
- Create: `apps/api/Auth/ClerkAuthentication.cs`
- Modify: `apps/api/Program.cs`
- Modify: `apps/api/Endpoints/StoryEndpoints.cs:33`
- Modify: `apps/api/appsettings.json`, `apps/api/appsettings.Development.json.example`
- Modify: `docker-compose.yml`, `.env.example`
- Modify: `tests/StoryWalker.Api.Tests/StoryWalkerApiFactory.cs`
- Test: `tests/StoryWalker.Api.Tests/AuthenticationTests.cs` (new)
- Modify: `tests/StoryWalker.Api.Tests/StoryEndpointsTests.cs`, `tests/StoryWalker.Api.Tests/AgentEndpointsTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `StoryWalker.Api.Auth.ClerkAuthentication.AddClerkAuthentication(this IServiceCollection, string authority, IReadOnlyCollection<string> authorizedParties) -> IServiceCollection`
  - `StoryWalkerApiFactory.CreateClientAs(string clerkUserId, string email = "test@storywalker.app") -> HttpClient`
  - `StoryWalkerApiFactory.TestAuthorizedParty` (const string), `StoryWalkerApiFactory.TestIssuer` (const string)
  - `StoryWalkerApiFactory.CreateToken(string clerkUserId, string email, string authorizedParty) -> string`

- [ ] **Step 1: Add the JwtBearer package centrally**

In `Directory.Packages.props`, inside the `Label="API"` group:

```xml
  <ItemGroup Label="API">
    <PackageVersion Include="Swashbuckle.AspNetCore" Version="10.2.3" />
    <!-- Validates Clerk-issued session tokens as a standard OIDC resource server (ADR-0002). -->
    <PackageVersion Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.9" />
  </ItemGroup>
```

In `apps/api/StoryWalker.Api.csproj`, in the first `ItemGroup`:

```xml
    <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" />
```

The test project reaches it transitively through its `ProjectReference` to the API, so its csproj needs nothing.

- [ ] **Step 2: Write the failing tests**

Create `tests/StoryWalker.Api.Tests/AuthenticationTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;

namespace StoryWalker.Api.Tests;

/// <summary>
/// Covers the JWT bearer middleware itself: the production configuration runs in these tests, with
/// only the signing key swapped for an in-memory one, so a broken issuer or azp rule fails here.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AuthenticationTests(StoryWalkerApiFactory factory)
{
    private readonly StoryWalkerApiFactory _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Health_is_anonymous()
    {
        var response = await _factory.CreateClient().GetAsync("/health", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Stories_without_a_token_returns_401()
    {
        var response = await _factory.CreateClient().GetAsync("/stories", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Stories_with_a_token_from_an_unauthorized_party_returns_401()
    {
        var client = _factory.CreateClient();
        var token = StoryWalkerApiFactory.CreateToken(
            "user_evil",
            "evil@storywalker.app",
            authorizedParty: "https://evil.example"
        );
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/stories", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Stories_with_a_valid_token_returns_200()
    {
        await _factory.ResetAsync();

        var response = await _factory.CreateClientAs("user_alice").GetAsync("/stories", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/StoryWalker.Api.Tests --filter "FullyQualifiedName~AuthenticationTests"`
Expected: FAIL — `CreateClientAs` and `CreateToken` do not exist (compile error).

- [ ] **Step 4: Write the authentication extension**

Create `apps/api/Auth/ClerkAuthentication.cs`:

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace StoryWalker.Api.Auth;

/// <summary>
/// Registers Clerk-issued session tokens as the API's bearer credentials (ADR-0002). Clerk is an
/// ordinary OIDC issuer with one twist: its session tokens carry no <c>aud</c> claim, so audience
/// validation is off and the <c>azp</c> claim — the origin that obtained the token — takes its
/// place. Skipping that check would let any site holding a Clerk token for another application
/// call this API, so it is a hard failure, not a warning.
/// </summary>
public static class ClerkAuthentication
{
    public static IServiceCollection AddClerkAuthentication(
        this IServiceCollection services,
        string authority,
        IReadOnlyCollection<string> authorizedParties
    )
    {
        var parties = new HashSet<string>(authorizedParties, StringComparer.Ordinal);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Authority drives JWKS discovery; the tests null it out and supply a key directly.
                options.Authority = authority;

                // Read `sub`, `azp` and `email` under their JWT names instead of the WS-Federation
                // URIs .NET maps them to by default.
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = authority,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    // Clerk session tokens live 60 seconds, and signing out cannot revoke one that
                    // is already issued — it only stops the next one being minted. This tolerance
                    // is therefore added straight onto the window in which a signed-out token still
                    // works, so it stays small. .NET's default is 5 minutes, which would stretch a
                    // 60-second token to six; raising it back "to be safe" widens exactly the gap
                    // the short lifetime exists to close.
                    ClockSkew = TimeSpan.FromSeconds(5),
                };

                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        var azp = context.Principal?.FindFirst("azp")?.Value;
                        if (azp is null || !parties.Contains(azp))
                        {
                            context.Fail("The 'azp' claim is not an authorized party.");
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        services.AddAuthorization();

        return services;
    }
}
```

- [ ] **Step 5: Wire it into Program.cs**

In `apps/api/Program.cs`, after the `AddFoundryChat` call and before `AddEndpointsApiExplorer`:

```csharp
// Clerk-issued session tokens are the API's credentials (ADR-0002). Blank counts as unconfigured
// for the same reason as Foundry above: compose always defines the variables.
var clerkAuthority = builder.Configuration["Clerk:Authority"];
if (string.IsNullOrWhiteSpace(clerkAuthority))
{
    throw new InvalidOperationException("'Clerk:Authority' is not configured.");
}

var authorizedParties = (builder.Configuration["Clerk:AuthorizedParties"] ?? string.Empty).Split(
    ',',
    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
);
if (authorizedParties.Length == 0)
{
    throw new InvalidOperationException("'Clerk:AuthorizedParties' is not configured.");
}

builder.Services.AddClerkAuthentication(clerkAuthority, authorizedParties);
```

Add `using StoryWalker.Api.Auth;` at the top. After `var app = builder.Build();` and before `app.MapGet("/health", ...)`:

```csharp
app.UseAuthentication();
app.UseAuthorization();
```

- [ ] **Step 6: Require authorization on the story endpoints**

In `apps/api/Endpoints/StoryEndpoints.cs`, replace line 33:

```csharp
        var stories = app.MapGroup("/stories").WithTags("Stories").RequireAuthorization();
```

- [ ] **Step 7: Add the configuration keys**

`apps/api/appsettings.json` — add after the `ServiceBus` block:

```json
  "Clerk": {
    "Authority": "",
    "AuthorizedParties": ""
  }
```

`apps/api/appsettings.Development.json.example` — add the same block with local values:

```json
  "Clerk": {
    "Authority": "https://your-instance.clerk.accounts.dev",
    "AuthorizedParties": "http://localhost:3000"
  }
```

`docker-compose.yml`, in the `api` service's `environment:` block, after `ServiceBus__ConnectionString`:

```yaml
      # Clerk session tokens are the API's credentials (ADR-0002). The authority is the instance
      # URL (JWKS is discovered from it); authorized parties is the comma-separated list of origins
      # allowed to obtain tokens for this API — the Next.js app in dev.
      Clerk__Authority: "${CLERK_AUTHORITY:-}"
      Clerk__AuthorizedParties: "${CLERK_AUTHORIZED_PARTIES:-http://localhost:3000}"
```

`.env.example` — append:

```bash
# --- Clerk authentication (ADR-0002) ----------------------------------------
# The instance URL from the Clerk dashboard; the API discovers JWKS from it and refuses to start
# without it. Not a secret — the API only ever verifies signatures, never mints tokens.
CLERK_AUTHORITY=

# Comma-separated origins allowed to obtain tokens for this API, matched against the token's `azp`
# claim. Clerk issues no `aud` claim, so this is the audience check; leaving it wrong is a CSRF
# vector, not a convenience setting.
CLERK_AUTHORIZED_PARTIES=http://localhost:3000
```

- [ ] **Step 8: Teach the test factory to mint tokens**

In `tests/StoryWalker.Api.Tests/StoryWalkerApiFactory.cs`, add these usings:

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
```

Add these members to the class, above `ConfigureWebHost`:

```csharp
    /// <summary>Issuer the test tokens claim and the middleware is configured to accept.</summary>
    public const string TestIssuer = "https://clerk.test.invalid";

    /// <summary>The origin the test tokens carry as `azp`; the only authorized party in tests.</summary>
    public const string TestAuthorizedParty = "http://localhost:3000";

    // Symmetric, in-memory: the production middleware still validates the signature, it simply is
    // not fetching a JWKS over the network. HS256 needs at least 256 bits of key material.
    private static readonly SymmetricSecurityKey TestSigningKey = new(
        Encoding.UTF8.GetBytes("story-walker-integration-tests-signing-key-0001")
    );

    /// <summary>Mints a Clerk-shaped session token: `sub`, `email` and `azp`, signed with the test key.</summary>
    public static string CreateToken(
        string clerkUserId,
        string email = "test@storywalker.app",
        string authorizedParty = TestAuthorizedParty
    )
    {
        var token = new JwtSecurityToken(
            issuer: TestIssuer,
            claims:
            [
                new Claim("sub", clerkUserId),
                new Claim("email", email),
                new Claim("azp", authorizedParty),
            ],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                TestSigningKey,
                SecurityAlgorithms.HmacSha256
            )
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>A client authenticated as the given Clerk subject.</summary>
    public HttpClient CreateClientAs(string clerkUserId, string email = "test@storywalker.app")
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateToken(clerkUserId, email)
        );

        return client;
    }
```

In `ConfigureWebHost`, add the two settings next to the existing `UseSetting` calls:

```csharp
        // Clerk config so Program's guards pass. The authority is never dialled: the test services
        // below null it out and hand the middleware an in-memory key instead.
        builder.UseSetting("Clerk:Authority", TestIssuer);
        builder.UseSetting("Clerk:AuthorizedParties", TestAuthorizedParty);
```

And inside the existing `builder.ConfigureTestServices(services => { ... })` block, after the two `AddSingleton` lines:

```csharp
            // Swap JWKS discovery for a local key. This runs after the app's own Configure delegate
            // and before JwtBearer's post-configure, so clearing Authority stops a ConfigurationManager
            // from ever being created — nothing leaves the process. Everything else about the
            // middleware, including the azp check, is the production configuration.
            services.Configure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    options.Authority = null;
                    options.RequireHttpsMetadata = false;
                    options.TokenValidationParameters.IssuerSigningKey = TestSigningKey;
                    options.TokenValidationParameters.ValidateIssuerSigningKey = true;
                }
            );
```

- [ ] **Step 9: Authenticate the existing test suites**

In `tests/StoryWalker.Api.Tests/StoryEndpointsTests.cs` line 12, replace the client field:

```csharp
    private readonly HttpClient _client = factory.CreateClientAs("user_story_tests");
```

In `tests/StoryWalker.Api.Tests/AgentEndpointsTests.cs` line 19, make the same replacement with a different subject:

```csharp
    private readonly HttpClient _client = factory.CreateClientAs("user_agent_tests");
```

The `Health_returns_healthy` test in `StoryEndpointsTests` keeps using the authenticated client — `/health` is anonymous, and an authenticated call to an anonymous endpoint still succeeds.

- [ ] **Step 10: Run the full API suite**

Run: `dotnet test tests/StoryWalker.Api.Tests`
Expected: PASS, including the four new `AuthenticationTests`.

- [ ] **Step 11: Commit**

```bash
git add Directory.Packages.props apps/api .env.example docker-compose.yml tests/StoryWalker.Api.Tests
git commit -m "AUTH: - validate Clerk JWTs on the API; - require authorization on story endpoints."
```

---

### Task 2: Just-in-time user provisioning

The authenticated caller gets a local `User` row on first sight, and new stories are attributed to it instead of to "whatever user happens to be first in the database".

**Files:**
- Create: `apps/api/Auth/CurrentUser.cs`
- Modify: `apps/api/Program.cs`
- Modify: `apps/api/Endpoints/StoryEndpoints.cs` (the conversations handler and `StartGenerationAsync`)
- Test: `tests/StoryWalker.Api.Tests/CurrentUserTests.cs` (new)
- Modify: `tests/StoryWalker.Api.Tests/AgentEndpointsTests.cs`

**Interfaces:**
- Consumes: `StoryWalkerApiFactory.CreateClientAs` (Task 1).
- Produces: `StoryWalker.Api.Auth.CurrentUser`, a scoped service with `Task<Guid> GetIdAsync(CancellationToken ct = default)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/StoryWalker.Api.Tests/CurrentUserTests.cs`:

```csharp
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using StoryWalker.Application.Api;

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

    [Fact]
    public async Task Concurrent_first_requests_create_one_user()
    {
        await _factory.ResetAsync();

        // Separate clients so the two requests cannot share a scope. The unique index on
        // ClerkUserId decides the race; the loser must re-read rather than surface a 500.
        var responses = await Task.WhenAll(
            _factory
                .CreateClientAs("user_race")
                .PostAsJsonAsync("/stories/conversations", Turn("ciao"), Ct),
            _factory
                .CreateClientAs("user_race")
                .PostAsJsonAsync("/stories/conversations", Turn("ciao"), Ct)
        );

        Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode));
        var count = await _factory.ExecuteDbAsync(db =>
            db.Users.CountAsync(u => u.ClerkUserId == "user_race", Ct)
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
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/StoryWalker.Api.Tests --filter "FullyQualifiedName~CurrentUserTests"`
Expected: FAIL — no `User` row is created (`SingleAsync` throws), and the last test fails with 503 "No user exists to own the story".

Note the driver: the endpoint must resolve the caller on **every** turn, not only the one that authors a brief — otherwise an interview that never finishes would leave the user unprovisioned. Step 5 puts the resolution at the top of the handler for that reason.

- [ ] **Step 3: Write the CurrentUser service**

Create `apps/api/Auth/CurrentUser.cs`:

```csharp
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
```

- [ ] **Step 4: Register it**

In `apps/api/Program.cs`, immediately after the `AddClerkAuthentication` call:

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();
```

- [ ] **Step 5: Attribute new stories to the caller**

In `apps/api/Endpoints/StoryEndpoints.cs`, add `using StoryWalker.Api.Auth;`.

Change the conversations handler's parameter list to take `CurrentUser currentUser` (add it after `IBriefPublisher publisher`). Resolve the caller as the first statement inside the `try`, so every turn provisions the user and not just the one that authors a brief:

```csharp
                    try
                    {
                        var me = await currentUser.GetIdAsync(ct);

                        var turn = await agent.SendAsync(
                            request.ConversationId,
                            request.Message.Trim(),
                            ct
                        );
```

Then replace the `StartGenerationAsync` call site:

```csharp
                        if (turn is { BriefReady: true, Brief: not null })
                        {
                            var story = await StartGenerationAsync(db, publisher, me, turn.Brief, ct);

                            return Results.Accepted(
                                $"/stories/{story.Id}",
                                new StoryQueuedResponse(story.Id, story.Status.ToString())
                            );
                        }
```

Replace `StartGenerationAsync` entirely — it no longer returns null, so the 503 branch and the nullable return go away:

```csharp
    /// <summary>
    /// Persists a <see cref="Story"/> in <see cref="StoryStatus.Generating"/> from the authored brief
    /// and enqueues that brief for the generation worker (ADR-0003). Only the story row is created
    /// here; the worker's <c>PersistPages</c> activity writes the pages.
    /// </summary>
    private static async Task<Story> StartGenerationAsync(
        StoryWalkerDbContext db,
        IBriefPublisher publisher,
        Guid userId,
        GenerationBrief brief,
        CancellationToken ct
    )
    {
        var story = new Story
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = brief.Title,
            TargetAge = brief.TargetAge,
            Status = StoryStatus.Generating,
            ArtStyle = brief.ArtStyle,
        };

        db.Stories.Add(story);
        await db.SaveChangesAsync(ct);

        // Dual-write: the story is committed before the enqueue. A transactional outbox would make
        // this atomic (deferred); if the publish throws, the request fails and the story is left in
        // Generating for a later retry/cleanup.
        await publisher.PublishAsync(story.Id, brief, ct);

        return story;
    }
```

- [ ] **Step 6: Drop the now-dead seeding from the agent test**

In `tests/StoryWalker.Api.Tests/AgentEndpointsTests.cs` around line 80, delete these two lines from `BriefReady_persists_generating_story_publishes_brief_and_returns_202`:

```csharp
        // Seeds the owner user the endpoint attributes the new story to (auth is deferred).
        await _factory.ExecuteDbAsync(DbSeeder.SeedAsync);
```

The owner now comes from the token. Keep the `await _factory.ResetAsync();` line above them.

- [ ] **Step 7: Run the full API suite**

Run: `dotnet test tests/StoryWalker.Api.Tests`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add apps/api tests/StoryWalker.Api.Tests
git commit -m "AUTH: - provision the local user just-in-time; - own new stories by the caller."
```

---

### Task 3: Scope story reads and writes by owner

Another user's story is indistinguishable from one that does not exist.

**Files:**
- Modify: `apps/api/Endpoints/StoryEndpoints.cs` (list, get, patch, delete)
- Modify: `apps/api/Program.cs` (drop startup seeding)
- Delete: `src/StoryWalker.Infrastructure/Persistence/DbSeeder.cs`
- Modify: `tests/StoryWalker.Api.Tests/SeedData.cs` (absorbs the seeder)
- Modify: `tests/StoryWalker.Api.Tests/StoryEndpointsTests.cs`

**Interfaces:**
- Consumes: `CurrentUser.GetIdAsync` (Task 2).
- Produces: `SeedData.SeedDemoStoryAsync(StoryWalkerDbContext db, Guid ownerUserId) -> Task<Guid>` (returns the story id) and `SeedData.SeedUserAsync(StoryWalkerDbContext db, string clerkUserId) -> Task<Guid>` (returns the user id).

- [ ] **Step 1: Write the failing tests**

Add to `tests/StoryWalker.Api.Tests/StoryEndpointsTests.cs`:

```csharp
    [Fact]
    public async Task GetStories_excludes_other_users_stories()
    {
        await _factory.ResetAsync();
        var strangerId = await _factory.ExecuteDbAsync(db =>
            SeedData.SeedUserAsync(db, "user_stranger")
        );
        await _factory.ExecuteDbAsync(db => SeedData.SeedDemoStoryAsync(db, strangerId));

        var summaries = await _client.GetFromJsonAsync<List<StorySummaryDto>>("/stories", Ct);

        Assert.NotNull(summaries);
        Assert.Empty(summaries);
    }

    [Fact]
    public async Task GetStory_of_another_user_returns_404()
    {
        await _factory.ResetAsync();
        var strangerId = await _factory.ExecuteDbAsync(db =>
            SeedData.SeedUserAsync(db, "user_stranger")
        );
        var storyId = await _factory.ExecuteDbAsync(db =>
            SeedData.SeedDemoStoryAsync(db, strangerId)
        );

        var response = await _client.GetAsync($"/stories/{storyId}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PatchStory_of_another_user_returns_404_and_does_not_rename()
    {
        await _factory.ResetAsync();
        var strangerId = await _factory.ExecuteDbAsync(db =>
            SeedData.SeedUserAsync(db, "user_stranger")
        );
        var storyId = await _factory.ExecuteDbAsync(db =>
            SeedData.SeedDemoStoryAsync(db, strangerId)
        );

        var response = await _client.PatchAsJsonAsync(
            $"/stories/{storyId}",
            new UpdateStoryTitleRequest("Hijacked"),
            Ct
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var title = await _factory.ExecuteDbAsync(db =>
            db.Stories.Where(s => s.Id == storyId).Select(s => s.Title).SingleAsync(Ct)
        );
        Assert.NotEqual("Hijacked", title);
    }

    [Fact]
    public async Task DeleteStory_of_another_user_returns_404_and_keeps_the_story()
    {
        await _factory.ResetAsync();
        var strangerId = await _factory.ExecuteDbAsync(db =>
            SeedData.SeedUserAsync(db, "user_stranger")
        );
        var storyId = await _factory.ExecuteDbAsync(db =>
            SeedData.SeedDemoStoryAsync(db, strangerId)
        );

        var response = await _client.DeleteAsync($"/stories/{storyId}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(
            await _factory.ExecuteDbAsync(db => db.Stories.AnyAsync(s => s.Id == storyId, Ct))
        );
    }
```

Every existing test in this file that calls `SeedData.SeedDemoStoryAsync(db)` must now seed under the authenticated subject. Replace each such call with the two-step form, for example in `GetStories_returns_seeded_summary`:

```csharp
        await _factory.ResetAsync();
        var mineId = await _factory.ExecuteDbAsync(db =>
            SeedData.SeedUserAsync(db, "user_story_tests")
        );
        await _factory.ExecuteDbAsync(db => SeedData.SeedDemoStoryAsync(db, mineId));
```

`"user_story_tests"` is the subject `_client` authenticates as (Task 1, Step 9), so provisioning finds this row instead of creating a second one.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/StoryWalker.Api.Tests --filter "FullyQualifiedName~StoryEndpointsTests"`
Expected: FAIL — `SeedUserAsync` does not exist and `SeedDemoStoryAsync` takes one argument (compile error).

- [ ] **Step 3: Move the seeder into the test project**

Replace the whole contents of `tests/StoryWalker.Api.Tests/SeedData.cs` with the seeder, now owner-parameterised. Copy the page data verbatim from `src/StoryWalker.Infrastructure/Persistence/DbSeeder.cs` before deleting that file:

```csharp
using Microsoft.EntityFrameworkCore;
using StoryWalker.Domain.Entities;
using StoryWalker.Domain.Enums;
using StoryWalker.Infrastructure.Persistence;

namespace StoryWalker.Api.Tests;

/// <summary>
/// Test data builders that write directly to the database via a DbContext. This used to be
/// Infrastructure's DbSeeder, seeding demo data at API startup; once every story has a real owner
/// (ADR-0002) a story owned by a demo user is invisible to every account, so the tests became its
/// only consumer and it moved here.
/// <para>
/// Note how each page's illustration prompt restates the characters' appearance in full. That
/// repetition is the whole consistency mechanism now that per-character reference images are gone
/// (ADR-0019), so the seed data models the shape the agent is asked to produce.
/// </para>
/// </summary>
internal static class SeedData
{
    private const string Cast =
        "Luna, a round-faced girl with red curly hair in a yellow raincoat, "
        + "and Pip, a small grey kitten with bright green eyes";

    /// <summary>Inserts a user for the given Clerk subject and returns its id.</summary>
    public static async Task<Guid> SeedUserAsync(StoryWalkerDbContext db, string clerkUserId)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            ClerkUserId = clerkUserId,
            Email = $"{clerkUserId}@storywalker.app",
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return user.Id;
    }

    /// <summary>Inserts the demo story (6 complete pages) owned by <paramref name="ownerUserId"/>.</summary>
    public static async Task<Guid> SeedDemoStoryAsync(StoryWalkerDbContext db, Guid ownerUserId)
    {
        var story = new Story
        {
            Id = Guid.NewGuid(),
            UserId = ownerUserId,
            Title = "Luna and the Rainy Day Adventure",
            TargetAge = 5,
            Status = StoryStatus.Complete,
            ArtStyle = "soft watercolour, warm yellows and rainy blues",
        };

        var pages = new[]
        {
            NewPage(story.Id, 1,
                "Luna pressed her nose to the window. Outside, the rain drew silver lines down the glass.",
                $"{Cast}. Luna at a window watching the rain, Pip curled at her feet."),
            NewPage(story.Id, 2,
                "\"Come on, Pip!\" she said, pulling on her yellow raincoat. The puddles were waiting.",
                $"{Cast}. Luna by the front door in her raincoat, Pip looking up at her."),
            NewPage(story.Id, 3,
                "The first puddle was small. The second was bigger. The third went all the way over her boots.",
                $"{Cast}. Luna jumping into a wide puddle, Pip springing back from the splash."),
            NewPage(story.Id, 4,
                "Pip did not like the water at all, so Luna carried him in the crook of her arm.",
                $"{Cast}. Luna walking down a wet street carrying Pip against her chest."),
            NewPage(story.Id, 5,
                "Then the rain stopped, all at once, the way it sometimes does.",
                $"{Cast}. Luna and Pip standing still on a glistening pavement, looking up at a clearing sky."),
            NewPage(story.Id, 6,
                "A rainbow stretched over the whole street, and Luna decided rainy days were her favourite after all.",
                $"{Cast}. Luna and Pip beneath a wide rainbow arching over the rooftops."),
        };

        db.Stories.Add(story);
        db.Pages.AddRange(pages);
        await db.SaveChangesAsync();

        return story.Id;
    }

    private static Page NewPage(Guid storyId, int pageNo, string content, string illustrationPrompt) =>
        new()
        {
            Id = Guid.NewGuid(),
            StoryId = storyId,
            PageNo = pageNo,
            Content = content,
            IllustrationPrompt = illustrationPrompt,
            Status = PageStatus.Complete,
        };
}
```

Then delete the original: `git rm src/StoryWalker.Infrastructure/Persistence/DbSeeder.cs`

- [ ] **Step 4: Drop startup seeding**

In `apps/api/Program.cs`, the development block becomes migration-only:

```csharp
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // Apply the latest migration so the API is usable straight after a fresh `docker compose up`.
    // Development only — production migrates through a deliberate pipeline. Nothing is seeded: with
    // per-user ownership (ADR-0002) a demo story belongs to no real account, so a first sign-in
    // starts on an empty dashboard by design.
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<StoryWalkerDbContext>();
    await db.Database.MigrateAsync();
}
```

- [ ] **Step 5: Scope the four handlers**

In `apps/api/Endpoints/StoryEndpoints.cs`, rewrite each handler's signature to take `CurrentUser currentUser, CancellationToken ct` and filter on the owner.

`ListStories`:

```csharp
                async (StoryWalkerDbContext db, CurrentUser currentUser, CancellationToken ct) =>
                {
                    var me = await currentUser.GetIdAsync(ct);

                    var summaries = await db
                        .Stories.Where(s => s.UserId == me)
                        .OrderByDescending(s => s.CreatedAt)
                        .Select(s => new StorySummaryDto(
                            s.Id,
                            s.Title,
                            s.Status.ToString(),
                            s.CreatedAt
                        ))
                        .ToListAsync(ct);

                    return Results.Ok(summaries);
                }
```

`GetStory`:

```csharp
                async (
                    Guid id,
                    StoryWalkerDbContext db,
                    CurrentUser currentUser,
                    CancellationToken ct
                ) =>
                {
                    var me = await currentUser.GetIdAsync(ct);

                    // Owner filter inside the same query, so another user's story is a 404 rather
                    // than a 403: a 403 confirms the id exists, which the caller has no use for.
                    var story = await db
                        .Stories.Where(s => s.Id == id && s.UserId == me)
                        .Select(ToStoryDto)
                        .FirstOrDefaultAsync(ct);

                    return story is null ? Results.NotFound() : Results.Ok(story);
                }
```

`UpdateStoryTitle` — same treatment; the lookup becomes:

```csharp
                    var me = await currentUser.GetIdAsync(ct);
                    var story = await db.Stories.FirstOrDefaultAsync(
                        s => s.Id == id && s.UserId == me,
                        ct
                    );
```

and the re-projection at the end gains the same filter and `ct`:

```csharp
                    var updated = await db
                        .Stories.Where(s => s.Id == id && s.UserId == me)
                        .Select(ToStoryDto)
                        .FirstAsync(ct);
```

`DeleteStory` — same lookup change:

```csharp
                    var me = await currentUser.GetIdAsync(ct);
                    var story = await db.Stories.FirstOrDefaultAsync(
                        s => s.Id == id && s.UserId == me,
                        ct
                    );
```

Thread `ct` through the `SaveChangesAsync` calls in both handlers.

- [ ] **Step 6: Run the full API suite**

Run: `dotnet test tests/StoryWalker.Api.Tests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add apps/api src/StoryWalker.Infrastructure tests/StoryWalker.Api.Tests
git commit -m "AUTH: - scope stories by owner; - move the seeder into the test project."
```

---

### Task 4: Conversation ownership

A conversation id belonging to someone else is a 404, enforced where the conversation is loaded rather than by a check the endpoint must remember.

**Files:**
- Modify: `src/StoryWalker.Domain/Entities/Conversation.cs`
- Modify: `src/StoryWalker.Application/Agent/IStoryAgent.cs`
- Modify: `src/StoryWalker.Infrastructure/Agent/StoryAgent.cs`
- Modify: `src/StoryWalker.Infrastructure/Persistence/Configurations/ConversationConfiguration.cs`
- Create: `src/StoryWalker.Infrastructure/Persistence/Migrations/<timestamp>_ConversationOwner.cs` (generated)
- Modify: `apps/api/Endpoints/StoryEndpoints.cs` (the conversations handler)
- Modify: `tests/StoryWalker.Api.Tests/FakeStoryAgent.cs`
- Modify: `tests/StoryWalker.Api.Tests/AgentEndpointsTests.cs`

**Interfaces:**
- Consumes: `CurrentUser.GetIdAsync` (Task 2).
- Produces: `IStoryAgent.SendAsync(Guid userId, string? conversationId, string message, CancellationToken ct = default) -> Task<AgentTurnResult>`.

- [ ] **Step 1: Write the failing test**

Add to `tests/StoryWalker.Api.Tests/AgentEndpointsTests.cs`:

```csharp
    [Fact]
    public async Task Continuing_another_users_conversation_returns_404()
    {
        await _factory.ResetAsync();

        // The owner starts a conversation and gets its id back.
        var started = await _client.PostAsJsonAsync(
            "/stories/conversations",
            new AgentMessageRequest(ConversationId: null, Message: "ciao"),
            Ct
        );
        var owned = await started.Content.ReadFromJsonAsync<AgentMessageResponse>(Ct);
        Assert.NotNull(owned);

        // A different signed-in user presents that id.
        var stranger = _factory.CreateClientAs("user_conversation_thief");
        var response = await stranger.PostAsJsonAsync(
            "/stories/conversations",
            new AgentMessageRequest(owned.ConversationId, "continua"),
            Ct
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/StoryWalker.Api.Tests --filter "FullyQualifiedName~Continuing_another_users_conversation"`
Expected: FAIL with `200 OK` — the fake agent happily continues any id it issued.

- [ ] **Step 3: Make the owner required on the entity**

In `src/StoryWalker.Domain/Entities/Conversation.cs`:

```csharp
    /// <summary>The owning user (ADR-0002). Every conversation has one from its first turn.</summary>
    public Guid UserId { get; set; }
```

In `src/StoryWalker.Infrastructure/Persistence/Configurations/ConversationConfiguration.cs`, add inside `Configure`, after the `Status` property line:

```csharp
        // No navigation on User: conversations are reached through the agent, never listed per user.
        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => c.UserId);
```

- [ ] **Step 4: Change the agent interface and implementation**

In `src/StoryWalker.Application/Agent/IStoryAgent.cs`:

```csharp
    /// <summary>
    /// Sends one user turn and returns the agent's reply. Pass <paramref name="conversationId"/>
    /// as <c>null</c> to start a new conversation (the returned id continues it on later turns).
    /// A conversation belonging to another <paramref name="userId"/> is indistinguishable from one
    /// that does not exist: both raise <see cref="ConversationNotFoundException"/>.
    /// </summary>
    Task<AgentTurnResult> SendAsync(
        Guid userId,
        string? conversationId,
        string message,
        CancellationToken ct = default
    );
```

In `src/StoryWalker.Infrastructure/Agent/StoryAgent.cs`, change the signature to match, set the owner on creation:

```csharp
            conversation = new Conversation
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Status = ConversationStatus.Active,
            };
```

and pass the user through to the loader:

```csharp
            conversation = await LoadConversationAsync(userId, conversationId, ct);
```

```csharp
    private async Task<Conversation> LoadConversationAsync(
        Guid userId,
        string conversationId,
        CancellationToken ct
    )
    {
        // An unknown or malformed id is a not-found, so the client restarts cleanly instead of
        // silently continuing an empty conversation. Someone else's id takes the same path: the
        // owner filter lives in the lookup, so no caller can forget it.
        if (!Guid.TryParse(conversationId, out var id))
        {
            throw new ConversationNotFoundException(conversationId);
        }

        return await db
                .Conversations.Include(c => c.Messages)
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct)
            ?? throw new ConversationNotFoundException(conversationId);
    }
```

- [ ] **Step 5: Pass the caller from the endpoint**

In `apps/api/Endpoints/StoryEndpoints.cs`, the conversations handler already resolves `me` at the top of its `try` (Task 2). Pass it to the agent as well:

```csharp
                        var turn = await agent.SendAsync(
                            me,
                            request.ConversationId,
                            request.Message.Trim(),
                            ct
                        );
```

The `StartGenerationAsync` call below it already takes `me` and needs no change.

- [ ] **Step 6: Teach the fake agent about owners**

In `tests/StoryWalker.Api.Tests/FakeStoryAgent.cs`, change the issued-id map to record the owner and mirror the real agent's rule:

```csharp
    private readonly ConcurrentDictionary<string, Guid> _issued = new();

    public Task<AgentTurnResult> SendAsync(
        Guid userId,
        string? conversationId,
        string message,
        CancellationToken ct = default
    )
    {
        // Unknown id, or an id issued to another user: both are not-found, as in the real agent.
        if (
            !string.IsNullOrEmpty(conversationId)
            && (!_issued.TryGetValue(conversationId, out var owner) || owner != userId)
        )
        {
            throw new ConversationNotFoundException(conversationId);
        }

        var id = conversationId ?? Guid.NewGuid().ToString();
        _issued[id] = userId;

        return Task.FromResult(
            message == ConfirmMessage
                ? new AgentTurnResult(id, "Brief ready.", BriefReady: true, SampleBrief)
                : new AgentTurnResult(id, $"Echo: {message}", BriefReady: false)
        );
    }
```

Update the class's doc comment to mention the owner check.

- [ ] **Step 7: Generate the migration**

Run:

```bash
dotnet ef migrations add ConversationOwner \
  --project src/StoryWalker.Infrastructure \
  --startup-project apps/api \
  --output-dir Persistence/Migrations
```

Open the generated file and add a delete **before** the `AlterColumn` that makes `UserId` non-nullable. Existing rows have `NULL` there and would block the alter, and an in-flight interview has no owner to assign:

```csharp
            // Conversations predating ownership have no user to attribute them to, and an
            // unfinished interview is worth nothing once abandoned. Messages cascade.
            migrationBuilder.Sql("DELETE FROM \"Conversations\";");
```

- [ ] **Step 8: Run the full API suite**

Run: `dotnet test tests/StoryWalker.Api.Tests`
Expected: PASS. The Testcontainers database is migrated from scratch, so the new migration is exercised.

- [ ] **Step 9: Commit**

```bash
git add src apps/api tests/StoryWalker.Api.Tests
git commit -m "AUTH: - own conversations by user; - 404 on another user's conversation."
```

---

### Task 5: Clerk in the Next.js app

Signing in is required to reach the app. The dashboard stays at `/` for now; Task 7 moves it.

**Files:**
- Modify: `apps/ui/package.json`
- Create: `apps/ui/src/proxy.ts`
- Modify: `apps/ui/src/app/layout.tsx`
- Create: `apps/ui/src/app/sign-in/[[...sign-in]]/page.tsx`
- Create: `apps/ui/src/app/sign-up/[[...sign-up]]/page.tsx`
- Modify: `apps/ui/.env.example`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: a `clerkMiddleware` session available to server code via `auth()` from `@clerk/nextjs/server` — Task 6 depends on this existing.

- [ ] **Step 1: Install the packages**

Run: `cd apps/ui && npm install @clerk/nextjs @clerk/localizations`

- [ ] **Step 2: Read the Next.js 16 proxy guide**

Read `apps/ui/node_modules/next/dist/docs/01-app/01-getting-started/16-proxy.md`. This Next.js renamed `middleware.ts` to `proxy.ts`; the file must sit at `src/proxy.ts` (same level as `app/`), and only one such file is supported per project.

- [ ] **Step 3: Write the proxy**

Create `apps/ui/src/proxy.ts`:

```ts
import { clerkMiddleware, createRouteMatcher } from "@clerk/nextjs/server";

// Next 16 renamed middleware.ts to proxy.ts; clerkMiddleware() is unchanged, only the filename is.
const isPublicRoute = createRouteMatcher(["/sign-in(.*)", "/sign-up(.*)"]);

export default clerkMiddleware(async (auth, request) => {
  if (!isPublicRoute(request)) await auth.protect();
});

export const config = {
  matcher: [
    // Everything except Next's internals and static assets...
    "/((?!_next|[^?]*\\.(?:html?|css|js(?!on)|jpe?g|webp|png|gif|svg|ttf|woff2?|ico|csv|docx?|xlsx?|zip|webmanifest)).*)",
    // ...plus the API proxy route, which needs the session to mint a token...
    "/(api|trpc)(.*)",
    // ...and Clerk's own handshake path, which never reaches the session otherwise.
    "/__clerk/(.*)",
  ],
};
```

- [ ] **Step 4: Add the sign-in and sign-up pages**

Create `apps/ui/src/app/sign-in/[[...sign-in]]/page.tsx`:

```tsx
import { SignIn } from "@clerk/nextjs";

export default function SignInPage() {
  return (
    <div className="flex justify-center py-8">
      <SignIn />
    </div>
  );
}
```

Create `apps/ui/src/app/sign-up/[[...sign-up]]/page.tsx`:

```tsx
import { SignUp } from "@clerk/nextjs";

export default function SignUpPage() {
  return (
    <div className="flex justify-center py-8">
      <SignUp />
    </div>
  );
}
```

The optional catch-all segment (`[[...sign-in]]`) is required: Clerk routes its multi-step flows under these paths.

- [ ] **Step 5: Wrap the layout and add the account controls**

In `apps/ui/src/app/layout.tsx`, add the imports:

```tsx
import { ClerkProvider, SignedIn, SignedOut, UserButton } from "@clerk/nextjs";
import { itIT } from "@clerk/localizations";
```

Wrap the returned tree in `<ClerkProvider localization={itIT}>` (outside `<html>`), and replace the header's inner `<div>` with:

```tsx
          <div className="mx-auto flex max-w-6xl items-center justify-between px-4 py-4 sm:px-6">
            <Link href="/" className="font-display text-2xl font-semibold tracking-tight">
              Story Walker
            </Link>
            <div className="flex items-center gap-4">
              <SignedOut>
                <Link href="/sign-in" className="text-sm font-semibold hover:text-accent">
                  Accedi
                </Link>
                <Link
                  href="/sign-up"
                  className="rounded-xl bg-accent px-4 py-2 text-sm font-semibold text-accent-foreground transition hover:brightness-105"
                >
                  Registrati
                </Link>
              </SignedOut>
              <SignedIn>
                <UserButton />
              </SignedIn>
            </div>
          </div>
```

- [ ] **Step 6: Document the environment**

Replace `apps/ui/.env.example` with:

```bash
# Base URL of the StoryWalker API. Read only by the server-side proxy route at
# src/app/api/[...path]/route.ts — it never reaches the browser.
API_URL=http://localhost:5080

# --- Clerk (ADR-0002) -------------------------------------------------------
# Both keys come from the Clerk dashboard. The publishable key is public by design; the secret key
# is not — keep it in .env, which git ignores.
NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY=
CLERK_SECRET_KEY=

# Where Clerk's components send people. These match the routes under src/app/.
NEXT_PUBLIC_CLERK_SIGN_IN_URL=/sign-in
NEXT_PUBLIC_CLERK_SIGN_UP_URL=/sign-up
```

- [ ] **Step 7: Verify the existing UI suite still passes**

Run: `cd apps/ui && npm test`
Expected: PASS. Component tests render components directly, not the layout, so none of them enter a Clerk context.

- [ ] **Step 8: Commit**

```bash
git add apps/ui/package.json apps/ui/package-lock.json apps/ui/src apps/ui/.env.example
git commit -m "UI: - add Clerk provider, proxy and sign-in/sign-up routes."
```

---

### Task 6: The server-side token proxy

The browser's relative `/api/*` calls become authenticated API calls, without the token ever entering the bundle.

**Files:**
- Create: `apps/ui/src/app/api/[...path]/route.ts`
- Test: `apps/ui/src/app/api/[...path]/route.test.ts` (new)
- Modify: `apps/ui/vitest.config.mts`
- Modify: `apps/ui/next.config.ts`

**Interfaces:**
- Consumes: `auth()` from `@clerk/nextjs/server`, available because Task 5 installed `proxy.ts`.
- Produces: `GET`, `POST`, `PATCH`, `DELETE` handlers at `/api/:path*`.

- [ ] **Step 1: Split the vitest config into two projects**

Replace the `test` block of `apps/ui/vitest.config.mts` with:

```ts
  test: {
    projects: [
      {
        extends: true,
        test: {
          name: "browser",
          include: ["src/**/*.test.{ts,tsx}"],
          // Setting `exclude` replaces Vitest's default, so node_modules has to be restated.
          exclude: ["**/node_modules/**", "src/app/api/**"],
          setupFiles: ["./src/test/setup.ts"],
          browser: {
            enabled: true,
            provider: playwright(),
            headless: true,
            instances: [{ browser: "chromium" }],
            // Vitest defaults to a 414px-wide phone viewport; the dashboard grid and the `sm:` hover
            // behaviour of the card actions only exist above that, so pin a desktop viewport.
            viewport: { width: 1280, height: 800 },
          },
        },
      },
      {
        // Route Handlers are server code: they cannot run in the browser project, and the one under
        // test decides whether a request leaves the app with credentials, so it gets real coverage.
        extends: true,
        test: {
          name: "node",
          environment: "node",
          include: ["src/app/api/**/*.test.ts"],
        },
      },
    ],
  },
```

- [ ] **Step 2: Write the failing tests**

Create `apps/ui/src/app/api/[...path]/route.test.ts`:

```ts
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const getToken = vi.fn();
vi.mock("@clerk/nextjs/server", () => ({ auth: () => Promise.resolve({ getToken }) }));

const { GET, POST, DELETE } = await import("./route");

const params = (...path: string[]) => ({ params: Promise.resolve({ path }) });

describe("API proxy route", () => {
  beforeEach(() => {
    getToken.mockResolvedValue("test-token");
    vi.stubGlobal("fetch", vi.fn());
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.clearAllMocks();
  });

  it("forwards the session token as a Bearer header", async () => {
    vi.mocked(fetch).mockResolvedValue(new Response("[]", { status: 200 }));

    await GET(new Request("http://localhost:3000/api/stories"), params("stories"));

    const [url, init] = vi.mocked(fetch).mock.calls[0];
    expect(String(url)).toBe("http://localhost:5080/stories");
    expect(new Headers(init!.headers).get("authorization")).toBe("Bearer test-token");
  });

  it("propagates the method, body and status code", async () => {
    vi.mocked(fetch).mockResolvedValue(new Response(null, { status: 204 }));

    const response = await DELETE(
      new Request("http://localhost:3000/api/stories/abc", { method: "DELETE" }),
      params("stories", "abc"),
    );

    const [, init] = vi.mocked(fetch).mock.calls[0];
    expect(init!.method).toBe("DELETE");
    expect(response.status).toBe(204);
  });

  it("forwards the request body on a POST", async () => {
    vi.mocked(fetch).mockResolvedValue(new Response("{}", { status: 200 }));

    await POST(
      new Request("http://localhost:3000/api/stories/conversations", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ message: "ciao" }),
      }),
      params("stories", "conversations"),
    );

    const [, init] = vi.mocked(fetch).mock.calls[0];
    expect(init!.body).toBe('{"message":"ciao"}');
  });

  it("fails closed when there is no session", async () => {
    getToken.mockResolvedValue(null);

    const response = await GET(new Request("http://localhost:3000/api/stories"), params("stories"));

    expect(response.status).toBe(401);
    expect(fetch).not.toHaveBeenCalled();
  });
});
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `cd apps/ui && npx vitest run --project node`
Expected: FAIL — `./route` does not exist.

- [ ] **Step 4: Write the route handler**

Create `apps/ui/src/app/api/[...path]/route.ts`:

```ts
// Server-side proxy to the StoryWalker API (ADR-0002). The browser holds only a Clerk session
// cookie and calls relative /api/* URLs; this handler exchanges that session for a JWT and forwards
// the request. The token never enters the client bundle, and API_URL stays server-only.

import { auth } from "@clerk/nextjs/server";

const API_URL = process.env.API_URL ?? "http://localhost:5080";

type Context = { params: Promise<{ path: string[] }> };

async function proxy(request: Request, { params }: Context): Promise<Response> {
  const { getToken } = await auth();
  const token = await getToken();

  // Fail closed. The API would reject an anonymous request anyway, but answering here keeps a
  // request with no credentials off the network entirely.
  if (!token) return new Response(null, { status: 401 });

  const { path } = await params;
  const target = new URL(`${API_URL}/${path.join("/")}`);
  target.search = new URL(request.url).search;

  const headers = new Headers({ Authorization: `Bearer ${token}` });
  const contentType = request.headers.get("content-type");
  if (contentType) headers.set("content-type", contentType);

  const hasBody = request.method !== "GET" && request.method !== "HEAD";

  const response = await fetch(target, {
    method: request.method,
    headers,
    body: hasBody ? await request.text() : undefined,
    cache: "no-store",
  });

  // 204s carry no body, so passing response.body straight through stays valid.
  const responseHeaders = new Headers();
  const responseType = response.headers.get("content-type");
  if (responseType) responseHeaders.set("content-type", responseType);

  return new Response(response.body, { status: response.status, headers: responseHeaders });
}

export const GET = proxy;
export const POST = proxy;
export const PATCH = proxy;
export const DELETE = proxy;
```

- [ ] **Step 5: Run the node tests to verify they pass**

Run: `cd apps/ui && npx vitest run --project node`
Expected: PASS (4 tests).

- [ ] **Step 6: Remove the rewrite it replaces**

Replace `apps/ui/next.config.ts` with:

```ts
import type { NextConfig } from "next";

// /api/* is served by the Route Handler at src/app/api/[...path]/route.ts, which attaches the
// Clerk token before forwarding to the ASP.NET Core API. The browser only ever talks to this
// origin, so the API still needs no CORS policy.
const nextConfig: NextConfig = {};

export default nextConfig;
```

- [ ] **Step 7: Run the whole UI suite**

Run: `cd apps/ui && npm test`
Expected: PASS in both projects. The browser tests still intercept `/api/*` with MSW, which does not care that a rewrite is gone.

- [ ] **Step 8: Commit**

```bash
git add apps/ui/src apps/ui/vitest.config.mts apps/ui/next.config.ts
git commit -m "UI: - proxy /api through a route handler that attaches the Clerk token."
```

---

### Task 7: Public landing page, dashboard at /dashboard

**Files:**
- Create: `apps/ui/src/app/page.tsx` (new landing, replacing the dashboard's file)
- Move: `apps/ui/src/app/page.tsx` → `apps/ui/src/app/dashboard/page.tsx`
- Move: `apps/ui/src/app/page.test.tsx` → `apps/ui/src/app/dashboard/page.test.tsx`
- Test: `apps/ui/src/app/page.test.tsx` (new, for the landing)
- Modify: `apps/ui/src/proxy.ts`
- Modify: `apps/ui/src/app/stories/[id]/page.tsx:77,90`
- Modify: `apps/ui/src/app/layout.tsx`

**Interfaces:**
- Consumes: `isPublicRoute` in `src/proxy.ts` (Task 5), the `SignedIn`/`SignedOut` components (Task 5).
- Produces: routes `/` (public) and `/dashboard` (protected).

- [ ] **Step 1: Move the dashboard**

```bash
cd apps/ui
mkdir -p src/app/dashboard
git mv src/app/page.tsx src/app/dashboard/page.tsx
git mv src/app/page.test.tsx src/app/dashboard/page.test.tsx
```

Neither file's contents change: the component is still a client component with polling, and the test's assertions are about what it renders, not where it lives.

- [ ] **Step 2: Write the failing test for the landing**

Create `apps/ui/src/app/page.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import { expect, test } from "vitest";
import LandingPage from "./page";

test("invites a visitor to start and links to sign-up", () => {
  render(<LandingPage />);

  expect(screen.getByRole("heading", { level: 1 })).toHaveTextContent(/storie/i);
  expect(screen.getByRole("link", { name: /inizia/i })).toHaveAttribute("href", "/sign-up");
});

test("explains the three steps", () => {
  render(<LandingPage />);

  expect(screen.getAllByRole("listitem")).toHaveLength(3);
});
```

- [ ] **Step 3: Run it to verify it fails**

Run: `cd apps/ui && npx vitest run --project browser src/app/page.test.tsx`
Expected: FAIL — `./page` no longer exists (it moved in Step 1).

- [ ] **Step 4: Write the landing page**

Create `apps/ui/src/app/page.tsx`:

```tsx
import Link from "next/link";

// Public entry point. A server component with no data fetching: nothing here depends on who is
// looking, so it renders the same for a visitor and for a signed-in user (the header's UserButton
// is what tells them apart).
const STEPS = [
  {
    title: "Racconta l'idea",
    body: "Chiacchieri con l'agente: chi è il protagonista, dove si svolge, per quale bambino è.",
  },
  {
    title: "L'agente scrive il piano",
    body: "Trasforma la conversazione in un brief completo — titolo, età, stile illustrativo, sei pagine.",
  },
  {
    title: "Ricevi la storia",
    body: "Testo e illustrazioni vengono generati insieme, con personaggi coerenti da una pagina all'altra.",
  },
];

export default function LandingPage() {
  return (
    <div className="space-y-16 py-8">
      <section className="max-w-2xl space-y-6">
        <h1 className="font-display text-5xl font-semibold tracking-tight sm:text-6xl">
          Storie illustrate, scritte per il tuo bambino.
        </h1>
        <p className="text-lg text-muted">
          Descrivi l&apos;idea a voce tua. Story Walker la trasforma in una storia illustrata di sei
          pagine, con gli stessi personaggi dall&apos;inizio alla fine.
        </p>
        <Link
          href="/sign-up"
          className="inline-block rounded-xl bg-accent px-6 py-3 font-semibold text-accent-foreground shadow-sm transition hover:brightness-105"
        >
          Inizia
        </Link>
      </section>

      <section className="space-y-6">
        <h2 className="font-display text-2xl font-semibold tracking-tight">Come funziona</h2>
        <ol className="grid grid-cols-1 gap-4 sm:grid-cols-3">
          {STEPS.map((step, i) => (
            <li
              key={step.title}
              className="rounded-2xl border border-border bg-surface px-6 py-6 space-y-2"
            >
              <span className="font-display text-3xl font-semibold text-accent">{i + 1}</span>
              <h3 className="font-semibold">{step.title}</h3>
              <p className="text-sm text-muted">{step.body}</p>
            </li>
          ))}
        </ol>
      </section>
    </div>
  );
}
```

- [ ] **Step 5: Make `/` public and send signed-in users onward**

In `apps/ui/src/proxy.ts`, add `/` to the public matcher:

```ts
const isPublicRoute = createRouteMatcher(["/", "/sign-in(.*)", "/sign-up(.*)"]);
```

In `apps/ui/src/app/layout.tsx`, point the wordmark at the dashboard for signed-in users by replacing the single `<Link href="/">` with:

```tsx
            <SignedOut>
              <Link href="/" className="font-display text-2xl font-semibold tracking-tight">
                Story Walker
              </Link>
            </SignedOut>
            <SignedIn>
              <Link
                href="/dashboard"
                className="font-display text-2xl font-semibold tracking-tight"
              >
                Story Walker
              </Link>
            </SignedIn>
```

Also change the `<SignedIn>` block on the right to sit alongside `<UserButton />`:

```tsx
              <SignedIn>
                <Link href="/dashboard" className="text-sm font-semibold hover:text-accent">
                  Le tue storie
                </Link>
                <UserButton />
              </SignedIn>
```

- [ ] **Step 6: Repoint the back links in the story viewer**

In `apps/ui/src/app/stories/[id]/page.tsx`, change both `href="/"` occurrences (lines 77 and 90) to `href="/dashboard"`.

- [ ] **Step 7: Run the whole UI suite**

Run: `cd apps/ui && npm test`
Expected: PASS — the moved dashboard test, the two new landing tests, and everything else.

- [ ] **Step 8: Build to catch routing mistakes the tests cannot**

Run: `cd apps/ui && npm run build`
Expected: a successful build listing `/`, `/dashboard`, `/sign-in/[[...sign-in]]`, `/sign-up/[[...sign-up]]`, `/stories/[id]` and the `/api/[...path]` route. Requires `NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY` and `CLERK_SECRET_KEY` in `apps/ui/.env`.

- [ ] **Step 9: Commit**

```bash
git add apps/ui/src
git commit -m "UI: - add public landing page; - move the dashboard to /dashboard."
```

---

### Task 8: Record the decision

**Files:**
- Create: `docs/adr/0021-clerk-integration.md`
- Modify: `README.md`

**Interfaces:**
- Consumes: everything above.
- Produces: nothing code depends on.

- [ ] **Step 1: Read the ADR template and a recent ADR**

Read `docs/adr/0000-template.md` and `docs/adr/0020-plain-service-bus-function-for-generation.md` so the new file matches the house style (status line, deciders, the "things this decision obliges us to build or watch" section).

- [ ] **Step 2: Write ADR-0021**

Create `docs/adr/0021-clerk-integration.md` following that template. Status **Accepted**, date **2026-09-16**, deciders **Luca Martinelli**. It records the four integration decisions, each with the alternative it beat:

- **A server-side token proxy** (`app/api/[...path]/route.ts`) rather than a client-side Bearer header or a rewrite of the UI to Server Components. The token stays out of the browser bundle and `lib/api.ts` keeps its relative URLs.
- **Just-in-time user provisioning** rather than Clerk webhooks. No public tunnel in local development; the accepted gap is that deleting a Clerk account leaves our mirror row behind, to be closed before go-live.
- **404 rather than 403** for another user's row, with the owner filter inside the loading query so no handler can omit it.
- **Test-signed JWTs** rather than a fake authentication scheme or live Clerk tokens: the production middleware, `azp` check included, is what the suite exercises, and it stays offline (ADR-0017).

Note the two obligations it creates: the Clerk session token must carry a custom `email` claim, and SignalR will need to authenticate with the same token once it is wired.

Reference ADR-0002 as the decision this one implements, and the design doc at `docs/superpowers/specs/2026-09-16-clerk-auth-design.md`.

- [ ] **Step 3: Add it to the README index**

In `README.md`, append to the ADR list, after the 0020 entry:

```markdown
- [0021 — Clerk integration: server-side token proxy and JIT provisioning](docs/adr/0021-clerk-integration.md)
```

- [ ] **Step 4: Verify the whole repository is green**

Run: `dotnet test && cd apps/ui && npm test && npm run lint`
Expected: PASS on all three.

- [ ] **Step 5: Commit**

```bash
git add docs/adr/0021-clerk-integration.md README.md
git commit -m "DOCS: - add ADR-0021 on the Clerk integration."
```
