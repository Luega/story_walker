using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using StoryWalker.Infrastructure;
using StoryWalker.Infrastructure.Images;

namespace StoryWalker.Api.Tests;

/// <summary>
/// The worker is configured with one deployment name and nothing else, so that name alone has to
/// decide which of the two incompatible image routes Foundry serves is called (ADR-0011). These
/// tests pin that decision: FLUX names go to the Black Forest Labs provider route, everything else
/// to the Azure OpenAI images route.
/// </summary>
public sealed class FoundryImageRoutingTests
{
    [Theory]
    [InlineData("FLUX.2-pro")]
    [InlineData("FLUX.2-flex")]
    [InlineData("FLUX.1-Kontext-pro")]
    [InlineData("FLUX-1.1-pro")]
    [InlineData("flux.2-pro")]
    public void Flux_deployments_resolve_to_the_bfl_generator(string deployment) =>
        Assert.IsType<FluxImageGenerator>(Resolve(deployment));

    [Theory]
    [InlineData("gpt-image-1-mini")]
    [InlineData("gpt-image-1")]
    [InlineData("dall-e-3")]
    public void Non_flux_deployments_resolve_to_the_openai_generator(string deployment) =>
        Assert.IsType<AzureOpenAIImageGenerator>(Resolve(deployment));

    [Fact]
    public void Deployment_name_decides_the_route_without_extra_configuration()
    {
        // A custom deployment name is the case auto-detection cannot get right; it falls to the
        // OpenAI route, which answers 404 — permanent, so the renderer fails the page rather than
        // dead-lettering the story.
        Assert.IsType<AzureOpenAIImageGenerator>(Resolve("my-custom-deployment"));
    }

    [Fact]
    public void Flux_route_keeps_its_60_second_ceiling()
    {
        // FLUX.2-pro answered in 8-15 s across four stories; 60 s is still about four times the
        // slowest success, so a hang costs no more than it did.
        Assert.Equal(TimeSpan.FromSeconds(60), TimeoutFor("FLUX.2-pro", nameof(FluxImageGenerator)));
    }

    [Fact]
    public void Azure_openai_route_gets_room_for_slower_models()
    {
        // gpt-image-1.5 and gpt-image-1-mini answered in 29.7-41.4 s across three stories on
        // 2026-09-17. At 60 s the slowest success sat at two thirds of the ceiling.
        Assert.Equal(
            TimeSpan.FromSeconds(90),
            TimeoutFor("gpt-image-1-mini", nameof(AzureOpenAIImageGenerator))
        );
    }

    private static IImageGenerator Resolve(string deployment) =>
        Provider(deployment).GetRequiredService<IImageGenerator>();

    private static TimeSpan TimeoutFor(string deployment, string clientName) =>
        Provider(deployment).GetRequiredService<IHttpClientFactory>().CreateClient(clientName).Timeout;

    private static ServiceProvider Provider(string deployment) =>
        new ServiceCollection()
            .AddFoundryImages("https://res.services.ai.azure.com/", deployment, "test-key")
            .BuildServiceProvider();
}
