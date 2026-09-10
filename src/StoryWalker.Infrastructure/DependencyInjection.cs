using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using StoryWalker.Application.Agent;
using StoryWalker.Application.Messaging;
using StoryWalker.Infrastructure.Agent;
using StoryWalker.Infrastructure.Messaging;
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
    /// Registers the Service Bus <see cref="IBriefPublisher"/> used by the API to enqueue a validated
    /// brief (ADR-0003). The connection string is passed in by the host (read from its configuration),
    /// keeping Infrastructure free of configuration coupling — same convention as
    /// <see cref="AddInfrastructure"/>. The <see cref="ServiceBusClient"/> is a singleton so its
    /// connection is pooled across requests.
    /// </summary>
    public static IServiceCollection AddServiceBusPublishing(
        this IServiceCollection services,
        string connectionString
    )
    {
        services.AddSingleton(_ => new ServiceBusClient(connectionString));
        services.AddSingleton<IBriefPublisher, ServiceBusBriefPublisher>();

        return services;
    }

    /// <summary>
    /// Wires the <see cref="IStoryAgent"/> onto a Microsoft Foundry chat-model deployment
    /// (ADR-0011), reached through the Azure OpenAI client.
    /// <para>
    /// Authentication follows <paramref name="apiKey"/>: when supplied, the deployment key is used
    /// directly — the simple path for a container, which has no signed-in user to borrow an identity
    /// from. When omitted, <see cref="DefaultAzureCredential"/> resolves an identity instead: the
    /// developer's <c>az login</c> when running on a workstation, the app's managed identity once
    /// hosted in Azure. Identity is preferred where available, since it leaves no long-lived secret
    /// to store, rotate or leak.
    /// </para>
    /// The <see cref="IChatClient"/> is built lazily, so nothing authenticates or connects until the
    /// first conversation turn. The agent itself is scoped, because it persists history through the
    /// (scoped) <see cref="StoryWalkerDbContext"/> (ADR-0018).
    /// </summary>
    public static IServiceCollection AddFoundryChat(
        this IServiceCollection services,
        string endpoint,
        string deployment,
        string? apiKey = null
    )
    {
        services.AddSingleton<IChatClient>(_ =>
        {
            var client = string.IsNullOrWhiteSpace(apiKey)
                ? new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
                : new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));

            return client.GetChatClient(deployment).AsIChatClient();
        });
        services.AddScoped<IStoryAgent, StoryAgent>();

        return services;
    }
}
