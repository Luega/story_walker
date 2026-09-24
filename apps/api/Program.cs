using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StoryWalker.Api.Auth;
using StoryWalker.Api.Endpoints;
using StoryWalker.Infrastructure;
using StoryWalker.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(
    builder.Configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.")
);

// Publishes the authored brief onto the Service Bus ingestion queue when a conversation completes
// (ADR-0003). Locally this points at the Service Bus emulator; in the cloud, the namespace.
builder.Services.AddServiceBusPublishing(
    builder.Configuration["ServiceBus:ConnectionString"]
        ?? throw new InvalidOperationException("'ServiceBus:ConnectionString' is not configured.")
);

// Story agent backend: a Foundry chat-model deployment (ADR-0011), in every environment. Blank
// counts as unconfigured, not just null: docker-compose always defines these variables, so an unset
// one arrives as "" and would otherwise shadow the appsettings value and fail later, obscurely.
var foundryEndpoint = builder.Configuration["Foundry:Endpoint"];
var foundryDeployment = builder.Configuration["Foundry:Deployment"];

if (string.IsNullOrWhiteSpace(foundryEndpoint))
{
    throw new InvalidOperationException("'Foundry:Endpoint' is not configured.");
}

if (string.IsNullOrWhiteSpace(foundryDeployment))
{
    throw new InvalidOperationException("'Foundry:Deployment' is not configured.");
}

// The key is optional. Supplied (the container path), it authenticates on its own; omitted, the
// agent falls back to DefaultAzureCredential — your `az login` on a workstation, the app's managed
// identity once hosted in Azure.
builder.Services.AddFoundryChat(
    foundryEndpoint,
    foundryDeployment,
    builder.Configuration["Foundry:ApiKey"]
);

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

// Telemetry to Application Insights (ADR-0023): requests, outgoing HTTP (Foundry), Azure SDK calls
// (Service Bus), exceptions and ILogger output, all under one W3C trace. Opt-in: with no connection
// string nothing is registered, so the local loop and the tests run exactly as before. Blank counts
// as unset for the same compose reason as above. Sampling is the distro default; change it here
// through AzureMonitorOptions once traffic makes it worth it.
var appInsightsConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
{
    builder
        .Services.AddOpenTelemetry()
        .UseAzureMonitor(options => options.ConnectionString = appInsightsConnectionString)
        // The distro has no Postgres instrumentation of its own. Npgsql's spans carry the
        // parameterised SQL, never the parameter values.
        .WithTracing(tracing =>
            tracing
                .AddNpgsql()
                // The agent's gen_ai spans (AddFoundryChat). The prefix is Microsoft's, and will
                // drop once the GenAI semantic conventions are stable.
                .AddSource("Experimental.Microsoft.Extensions.AI")
        );
}

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

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

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok("healthy"));
app.MapStoryEndpoints();
app.MapUserEndpoints();

app.Run();
