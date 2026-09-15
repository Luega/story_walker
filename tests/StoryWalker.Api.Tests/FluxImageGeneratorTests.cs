using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using StoryWalker.Infrastructure.Images;

namespace StoryWalker.Api.Tests;

/// <summary>
/// Pure unit tests for <see cref="FluxImageGenerator"/> — no DB, no network. The request and response
/// shapes asserted here were captured from a real call to a FLUX.2-pro deployment on 2026-09-15:
/// POST {endpoint}/providers/blackforestlabs/v1/flux-2-pro?api-version=preview returned
/// <c>{"created":…,"data":[{"b64_json":"iVBOR…"}],"request_meta":{…}}</c>.
/// </summary>
public sealed class FluxImageGeneratorTests
{
    private const string OnePixelPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Posts_to_the_bfl_provider_route_with_bearer_key_and_json_body()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, SuccessBody());
        var generator = NewGenerator(handler, model: "FLUX.2-pro");

        await generator.GenerateImagesAsync("a red fox", cancellationToken: Ct);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "https://res.services.ai.azure.com/providers/blackforestlabs/v1/flux-2-pro?api-version=preview",
            request.Uri
        );
        Assert.Equal("Bearer test-key", request.Authorization);

        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal("FLUX.2-pro", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("a red fox", body.RootElement.GetProperty("prompt").GetString());
        Assert.Equal("png", body.RootElement.GetProperty("output_format").GetString());
        Assert.Equal(1, body.RootElement.GetProperty("num_images").GetInt32());
    }

    [Fact]
    public async Task Sends_a_content_length_header()
    {
        // The BFL gateway rejects a chunked body with 400 "no_content_length_header". Found by a
        // real end-to-end run on 2026-09-15: JsonContent streams and declares no length.
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

    [Theory]
    [InlineData("FLUX.2-pro", "flux-2-pro")]
    [InlineData("FLUX.2-flex", "flux-2-flex")]
    [InlineData("FLUX.1-Kontext-pro", "flux-kontext-pro")]
    [InlineData("FLUX-1.1-pro", "flux-pro-1.1")]
    public void Model_id_maps_to_the_documented_route_path(string model, string path)
    {
        // The model id and the URL path are NOT the same string (Foundry FLUX docs).
        Assert.Equal(path, FluxImageGenerator.RoutePathFor(model));
    }

    [Fact]
    public void Unknown_model_fails_loudly()
    {
        var ex = Assert.Throws<ArgumentException>(() => FluxImageGenerator.RoutePathFor("dall-e-3"));
        Assert.Contains("dall-e-3", ex.Message);
    }

    private static FluxImageGenerator NewGenerator(RecordingHandler handler, string model = "FLUX.2-pro") =>
        new(new HttpClient(handler), "https://res.services.ai.azure.com/", model, apiKey: "test-key");

    private static string SuccessBody() =>
        $$$"""{"created":1789477134,"data":[{"b64_json":"{{{OnePixelPngBase64}}}"}],"request_meta":{"cost":3.0}}""";

    private sealed record CapturedRequest(
        HttpMethod Method,
        string Uri,
        string? Authorization,
        long? ContentLength,
        string Body
    );

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
