using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OllamaSharp;
using StoryWalker.Application.Agent;
using StoryWalker.Infrastructure.Agent;
using StoryWalker.Infrastructure.Persistence;

namespace StoryWalker.Infrastructure;

/// <summary>
/// Composition root for the Infrastructure layer. Hosts (the API, later the Functions worker)
/// call <see cref="AddInfrastructure"/> to wire the persistence services.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers <see cref="StoryWalkerDbContext"/> backed by PostgreSQL. The connection string is
    /// passed in by the host (read from its configuration) so Infrastructure stays free of any
    /// configuration coupling.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString
    )
    {
        services.AddDbContext<StoryWalkerDbContext>(options => options.UseNpgsql(connectionString));

        return services;
    }

    /// <summary>
    /// Registers the <see cref="IStoryAgent"/>. Callers first register an <see cref="IChatClient"/>
    /// backend (<see cref="AddOllamaChat"/> or <see cref="AddFoundryChat"/>); the agent is
    /// provider-agnostic over it. Scoped, because it persists history through the (scoped)
    /// <see cref="StoryWalkerDbContext"/> (ADR-0018).
    /// </summary>
    private static IServiceCollection AddStoryAgentCore(this IServiceCollection services)
    {
        services.AddScoped<IStoryAgent, StoryAgent>();

        return services;
    }

    /// <summary>
    /// Wires the story agent onto a local Ollama server. The <see cref="IChatClient"/> is built
    /// lazily, so nothing connects until the first conversation turn.
    /// </summary>
    public static IServiceCollection AddOllamaChat(
        this IServiceCollection services,
        string endpoint,
        string model
    )
    {
        services.AddSingleton<IChatClient>(_ => new OllamaApiClient(new Uri(endpoint), model));

        return services.AddStoryAgentCore();
    }

    /// <summary>
    /// Wires the story agent onto a Microsoft Foundry chat-model deployment (ADR-0011), reached via
    /// the Azure OpenAI client with <see cref="DefaultAzureCredential"/>. Built lazily.
    /// </summary>
    public static IServiceCollection AddFoundryChat(
        this IServiceCollection services,
        string endpoint,
        string deployment
    )
    {
        services.AddSingleton<IChatClient>(_ =>
            new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
                .GetChatClient(deployment)
                .AsIChatClient()
        );

        return services.AddStoryAgentCore();
    }
}
