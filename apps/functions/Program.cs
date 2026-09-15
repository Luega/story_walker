using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StoryWalker.Infrastructure;

// Isolated-worker host for the generation worker. ConfigureFunctionsWorkerDefaults wires the
// Service Bus trigger discovered from the referenced packages; the function itself needs the EF
// model plus the Foundry and Blob adapters (ADR-0020).
var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(
        (context, services) =>
        {
            var config = context.Configuration;

            services.AddInfrastructure(Required(config, "ConnectionStrings:Postgres"));

            // Same credential rule as the API: an explicit key when supplied, otherwise
            // DefaultAzureCredential (developer az login locally, managed identity in Azure).
            services.AddFoundryImages(
                Required(config, "Foundry:Endpoint"),
                Required(config, "Foundry:ImageDeployment"),
                config["Foundry:ApiKey"]
            );

            services.AddImageStorage(
                Required(config, "Storage:ConnectionString"),
                config["Storage:Container"] ?? "story-images",
                Required(config, "Storage:PublicBaseUrl")
            );

            services.AddGeneration();
        }
    )
    .Build();

host.Run();

// Fail at startup rather than on the first message: a worker that starts and then dead-letters
// every brief is far harder to diagnose than one that refuses to boot.
static string Required(IConfiguration config, string key) =>
    config[key] is { Length: > 0 } value && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new InvalidOperationException(
            $"Configuration '{key}' is required by the generation worker but was not set."
        );
