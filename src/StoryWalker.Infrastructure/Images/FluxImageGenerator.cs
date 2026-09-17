using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Microsoft.Extensions.AI;

namespace StoryWalker.Infrastructure.Images;

/// <summary>
/// <see cref="IImageGenerator"/> for Black Forest Labs FLUX models deployed on Microsoft Foundry
/// (ADR-0011), speaking the BFL provider route:
/// <c>POST {endpoint}/providers/blackforestlabs/v1/{route-path}?api-version=preview</c>.
/// <para>
/// It exists because <c>AzureOpenAIClient.GetImageClient(...).AsIImageGenerator()</c> cannot reach
/// FLUX.2: that client always calls <c>/openai/deployments/{name}/images/generations</c>, and Foundry
/// serves the OpenAI-compatible Image API only for FLUX.1-Kontext-pro and FLUX-1.1-pro. Against a
/// FLUX.2-pro deployment it returns 404. Implementing the same abstraction keeps the renderer and its
/// tests unaware of which route is behind it.
/// </para>
/// </summary>
public sealed class FluxImageGenerator : IImageGenerator
{
    private const string CognitiveServicesScope = "https://cognitiveservices.azure.com/.default";

    // The model id and the URL path are NOT the same string — the Foundry FLUX docs call this out.
    private static readonly Dictionary<string, string> RoutePaths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FLUX.2-pro"] = "flux-2-pro",
        ["FLUX.2-flex"] = "flux-2-flex",
        ["FLUX.1-Kontext-pro"] = "flux-kontext-pro",
        ["FLUX-1.1-pro"] = "flux-pro-1.1",
    };

    private readonly HttpClient _http;
    private readonly Uri _requestUri;
    private readonly string _model;
    private readonly string? _apiKey;
    private readonly TokenCredential? _credential;

    /// <param name="endpoint">The Foundry resource endpoint, e.g. <c>https://&lt;name&gt;.services.ai.azure.com</c>.</param>
    /// <param name="model">The FLUX model id, e.g. <c>FLUX.2-pro</c>; sent as <c>model</c> and mapped to the route path.</param>
    /// <param name="apiKey">Sent as a bearer token when supplied.</param>
    /// <param name="credential">Used for a Microsoft Entra token when no key is supplied.</param>
    public FluxImageGenerator(
        HttpClient http,
        string endpoint,
        string model,
        string? apiKey = null,
        TokenCredential? credential = null
    )
    {
        if (string.IsNullOrWhiteSpace(apiKey) && credential is null)
        {
            throw new ArgumentException("Either an API key or a token credential is required.");
        }

        _http = http;
        _model = model;
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
        _credential = credential;
        _requestUri = new Uri(
            $"{endpoint.TrimEnd('/')}/providers/blackforestlabs/v1/{RoutePathFor(model)}?api-version=preview"
        );
    }

    /// <summary>
    /// Whether <paramref name="model"/> is a FLUX deployment this generator can call. It is what
    /// picks the route at startup: anything else belongs to the Azure OpenAI images route
    /// (<see cref="AzureOpenAIImageGenerator"/>), which is the other half of what Foundry serves.
    /// </summary>
    public static bool CanHandle(string model) => RoutePaths.ContainsKey(model);

    /// <summary>
    /// Maps a FLUX model id to its route path. Throws for anything else, so a deployment routed here
    /// by mistake fails at construction instead of 404-ing on every page.
    /// </summary>
    public static string RoutePathFor(string model) =>
        RoutePaths.TryGetValue(model, out var path)
            ? path
            : throw new ArgumentException(
                $"'{model}' is not a FLUX model this generator knows how to call. "
                    + $"Supported: {string.Join(", ", RoutePaths.Keys)}."
            );

    public async Task<ImageGenerationResponse> GenerateAsync(
        ImageGenerationRequest request,
        ImageGenerationOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        var size = options?.ImageSize;

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new FluxRequest(
                Model: _model,
                Prompt: request.Prompt ?? string.Empty,
                Width: size?.Width ?? 1024,
                Height: size?.Height ?? 1024,
                OutputFormat: "png",
                NumImages: 1
            )
        );

        // Buffered, not JsonContent: JsonContent streams the body without a Content-Length, and the
        // BFL gateway rejects that with 400 "no_content_length_header".
        using var message = new HttpRequestMessage(HttpMethod.Post, _requestUri)
        {
            Content = new ByteArrayContent(payload)
            {
                Headers = { ContentType = new MediaTypeHeaderValue("application/json") },
            },
        };
        message.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await BearerTokenAsync(cancellationToken)
        );

        using var response = await _http.SendAsync(message, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            // The status code has to survive on the exception: the renderer decides from it whether
            // the failure is worth retrying (429, 5xx) or permanent (404, 401, 400).
            throw new HttpRequestException(
                $"FLUX request to {_requestUri.AbsolutePath} failed with {(int)response.StatusCode} {response.StatusCode}: "
                    + Truncate(error, 500),
                inner: null,
                statusCode: response.StatusCode
            );
        }

        var body = await response.Content.ReadFromJsonAsync<FluxResponse>(cancellationToken)
            ?? throw new InvalidOperationException("FLUX returned an empty response body.");

        var contents = new List<AIContent>();
        foreach (var item in body.Data ?? [])
        {
            if (!string.IsNullOrEmpty(item.B64Json))
            {
                contents.Add(new DataContent(Convert.FromBase64String(item.B64Json), "image/png"));
            }
            else if (!string.IsNullOrEmpty(item.Url))
            {
                contents.Add(new UriContent(new Uri(item.Url), "image/png"));
            }
        }

        return new ImageGenerationResponse(contents) { RawRepresentation = body };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { }

    private async ValueTask<string> BearerTokenAsync(CancellationToken ct)
    {
        if (_apiKey is not null)
        {
            return _apiKey;
        }

        // DefaultAzureCredential caches and refreshes tokens itself; asking per request is cheap.
        var token = await _credential!.GetTokenAsync(new TokenRequestContext([CognitiveServicesScope]), ct);
        return token.Token;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";

    private sealed record FluxRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("width")] int Width,
        [property: JsonPropertyName("height")] int Height,
        [property: JsonPropertyName("output_format")] string OutputFormat,
        [property: JsonPropertyName("num_images")] int NumImages
    );

    private sealed record FluxResponse([property: JsonPropertyName("data")] List<FluxImage>? Data);

    private sealed record FluxImage(
        [property: JsonPropertyName("b64_json")] string? B64Json,
        [property: JsonPropertyName("url")] string? Url
    );
}
