using Microsoft.Extensions.AI;

namespace StoryWalker.Api.Tests;

/// <summary>
/// Test double for <see cref="IImageGenerator"/>: counts calls, records the prompts it was given,
/// and returns a fixed one-pixel PNG. Counting the calls is what lets a test assert the ADR-0005
/// idempotency contract — render twice, generate once.
/// </summary>
internal sealed class FakeImageGenerator : IImageGenerator
{
    /// <summary>Set to throw on the next call, to exercise the failure path.</summary>
    public Exception? ThrowOnGenerate { get; set; }

    /// <summary>
    /// With <see cref="ThrowOnGenerate"/> set, throw only for this many calls and then succeed —
    /// the shape of a throttle that clears.
    /// </summary>
    public int? FailuresBeforeSuccess { get; set; }

    public List<string> Prompts { get; } = [];

    public int CallCount => Prompts.Count;

    public Task<ImageGenerationResponse> GenerateAsync(
        ImageGenerationRequest request,
        ImageGenerationOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        Prompts.Add(request.Prompt ?? string.Empty);

        if (ThrowOnGenerate is not null && (FailuresBeforeSuccess is null || Prompts.Count <= FailuresBeforeSuccess))
        {
            throw ThrowOnGenerate;
        }

        return Task.FromResult(
            new ImageGenerationResponse([new DataContent(OnePixelPng, "image/png")])
        );
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }

    /// <summary>A real 1x1 PNG, so the store receives bytes that are actually an image.</summary>
    private static ReadOnlyMemory<byte> OnePixelPng =>
        Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="
        );
}

/// <summary>
/// In-memory <see cref="StoryWalker.Application.Images.IImageStore"/>: keeps the bytes it was handed
/// and returns a deterministic URL, so the render path can be tested without Azurite.
/// </summary>
internal sealed class InMemoryImageStore : StoryWalker.Application.Images.IImageStore
{
    public Dictionary<string, ReadOnlyMemory<byte>> Saved { get; } = [];

    public Task<string> SaveAsync(
        Guid storyId,
        int pageNo,
        ReadOnlyMemory<byte> content,
        string contentType,
        CancellationToken ct = default
    )
    {
        var url = $"https://images.test/stories/{storyId}/pages/{pageNo}.png";
        Saved[url] = content;
        return Task.FromResult(url);
    }
}
