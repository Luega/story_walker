using System.Net.Http;
using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StoryWalker.Application.Agent;
using StoryWalker.Application.Images;
using StoryWalker.Application.Messaging;
using StoryWalker.Infrastructure.Agent;
using StoryWalker.Infrastructure.Generation;
using StoryWalker.Infrastructure.Images;
using StoryWalker.Infrastructure.Messaging;
using StoryWalker.Infrastructure.Persistence;
using StoryWalker.Infrastructure.Quota;

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

        // The clock of the weekly story limit (ADR-0030) and of the stale-story sweep (ADR-0031),
        // injected so tests can stop it.
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<StoryQuotaService>();

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
    /// (ADR-0011), through <see cref="AddFoundryChatClient"/>. The agent itself is scoped, because
    /// it persists history through the (scoped) <see cref="StoryWalkerDbContext"/> (ADR-0018).
    /// </summary>
    public static IServiceCollection AddFoundryChat(
        this IServiceCollection services,
        string endpoint,
        string deployment,
        string? apiKey = null
    )
    {
        services.AddFoundryChatClient(endpoint, deployment, apiKey);
        services.AddScoped<IStoryAgent, StoryAgent>();

        return services;
    }

    /// <summary>
    /// Registers an <see cref="IChatClient"/> for a Microsoft Foundry chat-model deployment
    /// (ADR-0011), reached through the Azure OpenAI client. The API's agent talks through it, and
    /// so does the worker, which rewrites an illustration prompt the image model refused (ADR-0025).
    /// <para>
    /// Authentication follows <paramref name="apiKey"/>: when supplied, the deployment key is used
    /// directly — the simple path for a container, which has no signed-in user to borrow an identity
    /// from. When omitted, <see cref="DefaultAzureCredential"/> resolves an identity instead: the
    /// developer's <c>az login</c> when running on a workstation, the app's managed identity once
    /// hosted in Azure. Identity is preferred where available, since it leaves no long-lived secret
    /// to store, rotate or leak.
    /// </para>
    /// The client is built lazily, so nothing authenticates or connects until the first call.
    /// </summary>
    public static IServiceCollection AddFoundryChatClient(
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

            // A gen_ai span per turn, with the model, token counts and finish reason (ADR-0023).
            // Nothing listens unless the host registered telemetry, so this is free when it is off.
            // Sensitive data stays off explicitly — otherwise an OTEL_* environment variable could
            // switch it on — because the messages are a parent's words about their children.
            return client
                .GetChatClient(deployment)
                .AsIChatClient()
                .AsBuilder()
                .UseOpenTelemetry(configure: otel => otel.EnableSensitiveData = false)
                .Build();
        });

        return services;
    }

    /// <summary>
    /// Registers the Foundry image deployment as an <see cref="IImageGenerator"/>. Foundry serves two
    /// mutually exclusive image routes and the deployment name is all we have to tell them apart: a
    /// Black Forest Labs FLUX name goes to the BFL provider route (<see cref="FluxImageGenerator"/>),
    /// anything else to the Azure OpenAI images route (<see cref="AzureOpenAIImageGenerator"/>).
    /// Calling either route with the other's deployment answers 404.
    /// <para>
    /// Nothing above this line knows which one is behind it — <c>PageImageRenderer</c> sees only
    /// <see cref="IImageGenerator"/> — so switching models is a configuration change.
    /// </para>
    /// <para>
    /// Credentials follow the same rule as <see cref="AddFoundryChat"/>: an explicit key when supplied,
    /// otherwise <see cref="DefaultAzureCredential"/>.
    /// </para>
    /// </summary>
    public static IServiceCollection AddFoundryImages(
        this IServiceCollection services,
        string endpoint,
        string deployment,
        string? apiKey = null
    )
    {
        var useFlux = FluxImageGenerator.CanHandle(deployment);
        var clientName = useFlux ? nameof(FluxImageGenerator) : nameof(AzureOpenAIImageGenerator);

        // A hung request is retried (the renderer treats a timeout as transient), so the ceiling
        // decides what a hang costs; it is set per route because the two routes' models are ~4x apart.
        // FLUX.2-pro, 2026-09-15/16: successes in 8-15 s, but 2 of 8 requests never answered at all.
        // 60 s is about four times the slowest success. GPT-Image (gpt-image-1.5, gpt-image-1-mini),
        // 2026-09-17: successes in 29.7-41.4 s, so 60 s left a slow success at two thirds of the
        // ceiling; 90 s is about twice the slowest. Raising either one raises the worst case of a full
        // run — keep it inside functionTimeout in host.json (ADR-0020).
        var timeout = TimeSpan.FromSeconds(useFlux ? 60 : 90);
        services.AddHttpClient(clientName, http => http.Timeout = timeout);

        services.AddSingleton<IImageGenerator>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);
            var credential = string.IsNullOrWhiteSpace(apiKey) ? new DefaultAzureCredential() : null;

            return useFlux
                ? new FluxImageGenerator(http, endpoint, deployment, apiKey, credential)
                : new AzureOpenAIImageGenerator(http, endpoint, deployment, apiKey, credential);
        });

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
    /// Registers the services the worker's functions delegate to. Scoped, because they share the
    /// scoped <see cref="StoryWalkerDbContext"/>; the host opens a scope per invocation.
    /// </summary>
    public static IServiceCollection AddGeneration(this IServiceCollection services)
    {
        // Only used when a deployment hands back a URL instead of the image bytes.
        services.AddHttpClient();
        services.AddScoped<StoryPersister>();
        services.AddScoped<IllustrationPromptRewriter>();
        services.AddScoped<PageImageRenderer>();
        services.AddScoped<GenerationFailureRecorder>();
        services.AddScoped<StoryGenerator>();
        services.AddScoped<StaleGenerationSweeper>();

        return services;
    }
}
