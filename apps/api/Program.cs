using Microsoft.EntityFrameworkCore;
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

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // Apply the latest migration and seed demo data so the API is usable straight after a fresh
    // `docker compose up`. Development only — production migrates through a deliberate pipeline.
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<StoryWalkerDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db);
}

app.MapGet("/health", () => Results.Ok("healthy"));
app.MapStoryEndpoints();

app.Run();
