using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using StoryWalker.Infrastructure.Images;

namespace StoryWalker.Api.Tests;

/// <summary>
/// Pure unit tests for <see cref="AzureOpenAIImageGenerator"/> — no DB, no network. The route and payload
/// asserted here are the Azure OpenAI Image API as served by Foundry for <c>gpt-image-1</c> and
/// <c>gpt-image-1-mini</c>: POST {endpoint}/openai/deployments/{name}/images/generations, answering
/// <c>{"created":…,"data":[{"b64_json":"iVBOR…"}]}</c>. Base64 is the only shape it returns.
/// </summary>
public sealed class AzureOpenAIImageGeneratorTests
{
    private const string OnePixelPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Posts_to_the_deployment_images_route_with_the_api_key_header()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, SuccessBody());
        var generator = NewGenerator(handler, model: "gpt-image-1-mini");

        await generator.GenerateImagesAsync("a red fox", cancellationToken: Ct);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "https://res.services.ai.azure.com/openai/deployments/gpt-image-1-mini/images/generations"
                + "?api-version=2025-04-01-preview",
            request.Uri
        );
        // The Azure OpenAI route authenticates a key through api-key, not an Authorization bearer.
        Assert.Equal("test-key", request.Header("api-key"));
        Assert.Null(request.Authorization);

        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal("gpt-image-1-mini", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("a red fox", body.RootElement.GetProperty("prompt").GetString());
        Assert.Equal(1, body.RootElement.GetProperty("n").GetInt32());
    }

    [Fact]
    public async Task Never_sends_response_format()
    {
        // gpt-image-1 rejects response_format with 400: it always answers base64. The renderer asks
        // for ImageGenerationResponseFormat.Data, so the temptation to forward it is real.
        var handler = new RecordingHandler(HttpStatusCode.OK, SuccessBody());
        var generator = NewGenerator(handler);

        await generator.GenerateImagesAsync(
            "a red fox",
            new ImageGenerationOptions { Count = 1, ResponseFormat = ImageGenerationResponseFormat.Data },
            Ct
        );

        var request = Assert.Single(handler.Requests);
        using var body = JsonDocument.Parse(request.Body);
        Assert.False(body.RootElement.TryGetProperty("response_format", out _));
    }

    [Fact]
    public async Task Sends_the_requested_size_as_a_wxh_string()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, SuccessBody());
        var generator = NewGenerator(handler);

        await generator.GenerateImagesAsync(
            "a red fox",
            new ImageGenerationOptions { ImageSize = new System.Drawing.Size(1536, 1024) },
            Ct
        );

        var request = Assert.Single(handler.Requests);
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal("1536x1024", body.RootElement.GetProperty("size").GetString());
    }

    [Fact]
    public async Task Defaults_to_a_square_when_no_size_is_asked_for()
    {
        // PageImageRenderer sets no ImageSize, so this default is what every page actually renders at.
        var handler = new RecordingHandler(HttpStatusCode.OK, SuccessBody());
        var generator = NewGenerator(handler);

        await generator.GenerateImagesAsync("a red fox", cancellationToken: Ct);

        var request = Assert.Single(handler.Requests);
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal("1024x1024", body.RootElement.GetProperty("size").GetString());
    }

    [Fact]
    public async Task Sends_a_content_length_header()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, SuccessBody());
        var generator = NewGenerator(handler);

        await generator.GenerateImagesAsync("a red fox", cancellationToken: Ct);

        var request = Assert.Single(handler.Requests);
        Assert.NotNull(request.ContentLength);
        Assert.Equal(Encoding.UTF8.GetByteCount(request.Body), request.ContentLength);
    }

    [Fact]
    public async Task Maps_inline_base64_to_png_data_content()
    {
        var generator = NewGenerator(new RecordingHandler(HttpStatusCode.OK, SuccessBody()));

        var response = await generator.GenerateImagesAsync("a red fox", cancellationToken: Ct);

        var image = Assert.IsType<DataContent>(Assert.Single(response.Contents));
        Assert.Equal("image/png", image.MediaType);
        Assert.Equal(Convert.FromBase64String(OnePixelPngBase64), image.Data.ToArray());
    }

    [Fact]
    public async Task Maps_a_url_result_to_uri_content()
    {
        // gpt-image-1 only ever answers base64, but dall-e-3 on the same route answers a hosted URL.
        // Dropping it would hand the renderer an empty response instead of an image.
        var generator = NewGenerator(
            new RecordingHandler(HttpStatusCode.OK, """{"data":[{"url":"https://img.test/fox.png"}]}""")
        );

        var response = await generator.GenerateImagesAsync("a red fox", cancellationToken: Ct);

        var image = Assert.IsType<UriContent>(Assert.Single(response.Contents));
        Assert.Equal(new Uri("https://img.test/fox.png"), image.Uri);
    }

    [Fact]
    public async Task Non_success_status_throws_with_the_status_code_and_body()
    {
        var generator = NewGenerator(new RecordingHandler(HttpStatusCode.NotFound, "NOT FOUND"));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => generator.GenerateImagesAsync("a red fox", cancellationToken: Ct)
        );

        // The status must survive on the exception: the renderer decides retry-or-fail from it.
        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        Assert.Contains("NOT FOUND", ex.Message);
    }

    [Fact]
    public void Requires_a_key_or_a_credential()
    {
        Assert.Throws<ArgumentException>(
            () => new AzureOpenAIImageGenerator(new HttpClient(), "https://res.services.ai.azure.com/", "gpt-image-1-mini")
        );
    }

    private static AzureOpenAIImageGenerator NewGenerator(
        RecordingHandler handler,
        string model = "gpt-image-1-mini"
    ) => new(new HttpClient(handler), "https://res.services.ai.azure.com/", model, apiKey: "test-key");

    private static string SuccessBody() =>
        $$$"""{"created":1789477134,"data":[{"b64_json":"{{{OnePixelPngBase64}}}"}]}""";

    private sealed record CapturedRequest(
        HttpMethod Method,
        string Uri,
        string? Authorization,
        IReadOnlyDictionary<string, string> Headers,
        long? ContentLength,
        string Body
    )
    {
        public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
    }

    private sealed class RecordingHandler(HttpStatusCode status, string responseBody) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            // Read the declared length BEFORE the body: reading buffers the content and would let a
            // streamed body report a length it never actually sent on the wire.
            var declaredLength = request.Content?.Headers.ContentLength;
            Requests.Add(
                new CapturedRequest(
                    request.Method,
                    request.RequestUri!.ToString(),
                    request.Headers.Authorization?.ToString(),
                    request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value)),
                    declaredLength,
                    request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)
                )
            );
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
