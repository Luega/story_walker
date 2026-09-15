using System.Net.Http;
using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using StoryWalker.Application.Agent;
using StoryWalker.Application.Images;
using StoryWalker.Application.Messaging;
using StoryWalker.Infrastructure.Agent;
using StoryWalker.Infrastructure.Generation;
using StoryWalker.Infrastructure.Images;
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

    /// <summary>
    /// Registers the Foundry image deployment as an <see cref="IImageGenerator"/>. The deployment is a
    /// Black Forest Labs FLUX model called through its provider route (<see cref="FluxImageGenerator"/>),
    /// not <c>AzureOpenAIClient.GetImageClient</c>: that client targets
    /// <c>/openai/deployments/{name}/images/generations</c>, which Foundry does not serve for FLUX.2 and
    /// answers with 404.
    /// <para>
    /// Credentials follow the same rule as <see cref="AddFoundryChat"/>: an explicit key when supplied,
    /// otherwise <see cref="DefaultAzureCredential"/>. The model name is validated here, at startup,
    /// so a typo stops the worker immediately instead of failing every page of every story.
    /// </para>
    /// </summary>
    public static IServiceCollection AddFoundryImages(
        this IServiceCollection services,
        string endpoint,
        string deployment,
        string? apiKey = null
    )
    {
        FluxImageGenerator.RoutePathFor(deployment);

        // Measured against FLUX.2-pro on 2026-09-15: successful 1024x1024 renders answered in 8-10.5 s,
        // but 2 of 8 requests never answered at all and ran into the timeout. A hung request is
        // retried (the renderer treats a timeout as transient), so the ceiling decides what a hang
        // costs: 60 s, about six times the slowest success, instead of the minutes it would otherwise
        // burn per page.
        services.AddHttpClient(nameof(FluxImageGenerator), http => http.Timeout = TimeSpan.FromSeconds(60));

        services.AddSingleton<IImageGenerator>(sp => new FluxImageGenerator(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(FluxImageGenerator)),
            endpoint,
            deployment,
            apiKey,
            string.IsNullOrWhiteSpace(apiKey) ? new DefaultAzureCredential() : null
        ));

        return services;
    }

    /// <summary>
    /// Registers Blob Storage as the page-image store (ADR-0010). <paramref name="publicBaseUrl"/>
    /// is what the stored URL is composed from; it is a separate setting because the host the worker
    /// uploads through is not the host the image is later opened from (see <see cref="BlobImageStore"/>).
    /// </summary>
    public static IServiceCollection AddImageStorage(
        this IServiceCollection services,
        string connectionString,
        string containerName,
        string publicBaseUrl
    )
    {
        services.AddSingleton(_ => new BlobServiceClient(connectionString));
        services.AddSingleton<IImageStore>(sp => new BlobImageStore(
            sp.GetRequiredService<BlobServiceClient>(),
            containerName,
            publicBaseUrl
        ));

        return services;
    }

    /// <summary>
    /// Registers the services the Durable activities delegate to. Scoped, because both share the
    /// scoped <see cref="StoryWalkerDbContext"/>; the worker opens a scope per activity.
    /// </summary>
    public static IServiceCollection AddGeneration(this IServiceCollection services)
    {
        // Only used when a deployment hands back a URL instead of the image bytes.
        services.AddHttpClient();
        services.AddScoped<StoryPersister>();
        services.AddScoped<PageImageRenderer>();

        return services;
    }
}
