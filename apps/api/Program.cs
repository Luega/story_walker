using Microsoft.EntityFrameworkCore;
using StoryWalker.Api.Endpoints;
using StoryWalker.Infrastructure;
using StoryWalker.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(
    builder.Configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.")
);

// Story agent backend: local Ollama by default in Development, Foundry elsewhere. Both expose the
// same IChatClient, so the agent logic is identical — only the registered client differs.
var configuredProvider = builder.Configuration["Agent:Provider"];
var agentProvider = string.IsNullOrWhiteSpace(configuredProvider)
    ? (builder.Environment.IsDevelopment() ? "Ollama" : "Foundry")
    : configuredProvider;

if (string.Equals(agentProvider, "Ollama", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddOllamaChat(
        builder.Configuration["Ollama:Endpoint"] ?? "http://localhost:11434",
        builder.Configuration["Ollama:Model"] ?? "llama3.1"
    );
}
else
{
    builder.Services.AddFoundryChat(
        builder.Configuration["Foundry:Endpoint"]
            ?? throw new InvalidOperationException("'Foundry:Endpoint' is not configured."),
        builder.Configuration["Foundry:Deployment"]
            ?? throw new InvalidOperationException("'Foundry:Deployment' is not configured.")
    );
}

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
