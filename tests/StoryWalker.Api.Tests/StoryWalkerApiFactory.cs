using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Respawn;
using StoryWalker.Application.Agent;
using StoryWalker.Application.Messaging;
using StoryWalker.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace StoryWalker.Api.Tests;

/// <summary>
/// Boots the real API in memory (<see cref="WebApplicationFactory{TEntryPoint}"/>) against a
/// throwaway PostgreSQL started with Testcontainers, so the relational behaviour the endpoints rely
/// on (cascade deletes, EF→SQL projections) is exercised
/// for real. The container is migrated once; <see cref="ResetAsync"/> wipes data between tests.
/// </summary>
public sealed class StoryWalkerApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Matches the image used in docker-compose.yml so the test DB is identical to local/prod.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(
        "pgvector/pgvector:pg17"
    ).Build();

    private Respawner _respawner = null!;

    /// <summary>Records enqueued briefs so tests can assert the publish without a real Service Bus.</summary>
    public RecordingBriefPublisher Publisher { get; } = new();

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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" (not Development) so Program.cs's startup auto-migrate/seed block stays off —
        // this fixture owns migration and seeding for determinism. The connection string is supplied
        // via the ConnectionStrings__Postgres environment variable (set in InitializeAsync), which
        // CreateBuilder reads before builder.Build() — the same mechanism docker-compose uses.
        builder.UseEnvironment("Testing");

        // Placeholder Foundry settings so Program's config guards pass; the agent is replaced with a
        // fake below and the IChatClient is registered lazily, so nothing authenticates or connects.
        builder.UseSetting("Foundry:Endpoint", "https://foundry.invalid/");
        builder.UseSetting("Foundry:Deployment", "test-deployment");

        // A well-formed emulator connection string so Program's ServiceBus guard passes; the real
        // publisher is replaced below, so no Service Bus connection is ever opened.
        builder.UseSetting(
            "ServiceBus:ConnectionString",
            "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"
        );

        // Clerk config so Program's guards pass. The authority is never dialled: the test services
        // below null it out and hand the middleware an in-memory key instead.
        builder.UseSetting("Clerk:Authority", TestIssuer);
        builder.UseSetting("Clerk:AuthorizedParties", TestAuthorizedParty);

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IStoryAgent>(sp => new FakeStoryAgent(
                sp.GetRequiredService<IServiceScopeFactory>()
            ));
            services.AddSingleton<IBriefPublisher>(Publisher);

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
        });
    }

    /// <summary>Runs an action against a fresh scoped <see cref="StoryWalkerDbContext"/>.</summary>
    public async Task ExecuteDbAsync(Func<StoryWalkerDbContext, Task> action)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StoryWalkerDbContext>();
        await action(db);
    }

    /// <summary>Like <see cref="ExecuteDbAsync(Func{StoryWalkerDbContext, Task})"/> but returns a value.</summary>
    public async Task<T> ExecuteDbAsync<T>(Func<StoryWalkerDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StoryWalkerDbContext>();
        return await action(db);
    }

    /// <summary>Truncates all data (keeping the schema + migration history) for test isolation.</summary>
    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await _respawner.ResetAsync(connection);
    }

    // xUnit v3: IAsyncLifetime.InitializeAsync returns ValueTask, and DisposeAsync comes from
    // IAsyncDisposable (also ValueTask) — the same shape as WebApplicationFactory.DisposeAsync, so we
    // simply override it rather than implementing the interface explicitly.
    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Postgres",
            _postgres.GetConnectionString()
        );

        // First touch of Services builds the host (reading the connection string above).
        await ExecuteDbAsync(db => db.Database.MigrateAsync());

        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        _respawner = await Respawner.CreateAsync(
            connection,
            new RespawnerOptions
            {
                DbAdapter = DbAdapter.Postgres,
                SchemasToInclude = ["public"],
                // Never wipe EF's migration bookkeeping — the schema must survive resets.
                TablesToIgnore = ["__EFMigrationsHistory"],
            }
        );
    }

    public override async ValueTask DisposeAsync()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", null);
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }
}

/// <summary>Shares one container/factory across every test in the collection (started once).</summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<StoryWalkerApiFactory>
{
    public const string Name = "api";
}
