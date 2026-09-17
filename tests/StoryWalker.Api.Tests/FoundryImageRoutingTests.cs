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

    private static IImageGenerator Resolve(string deployment) =>
        new ServiceCollection()
            .AddFoundryImages("https://res.services.ai.azure.com/", deployment, "test-key")
            .BuildServiceProvider()
            .GetRequiredService<IImageGenerator>();
}
