using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Microsoft.Extensions.AI;

namespace StoryWalker.Infrastructure.Images;

/// <summary>
/// <see cref="IImageGenerator"/> for image models Foundry serves on the Azure OpenAI Image API
/// (ADR-0011) — <c>gpt-image-1</c>, <c>gpt-image-1-mini</c>, <c>dall-e-3</c> — speaking
/// <c>POST {endpoint}/openai/deployments/{deployment}/images/generations?api-version=…</c>.
/// <para>
/// It is hand-rolled for the same reason <see cref="FluxImageGenerator"/> is, from the other side:
/// <c>AzureOpenAIClient.GetImageClient(...).AsIImageGenerator()</c> would be the obvious route, but
/// the pinned Azure.AI.OpenAI 2.1.0 knows only api-versions <c>2024-06-01</c> and <c>2024-10-21</c>,
/// neither of which serves gpt-image-1 — and the newer SDKs are pre-release. Owning the request also
/// keeps <c>response_format</c> off the wire, which gpt-image-1 rejects outright.
/// </para>
/// </summary>
public sealed class AzureOpenAIImageGenerator : IImageGenerator
{
    private const string CognitiveServicesScope = "https://cognitiveservices.azure.com/.default";

    /// <summary>
    /// The oldest api-version that serves gpt-image-1. Foundry accepts a newer one; it is pinned
    /// rather than tracked so a service-side default cannot change the payload under us.
    /// </summary>
    public const string ApiVersion = "2025-04-01-preview";

    private readonly HttpClient _http;
    private readonly Uri _requestUri;
    private readonly string _model;
    private readonly string? _apiKey;
    private readonly TokenCredential? _credential;

    /// <param name="endpoint">The Foundry resource endpoint, e.g. <c>https://&lt;name&gt;.services.ai.azure.com</c>.</param>
    /// <param name="model">The deployment name, e.g. <c>gpt-image-1-mini</c>; it is both the URL segment and the <c>model</c> field.</param>
    /// <param name="apiKey">Sent as the <c>api-key</c> header when supplied.</param>
    /// <param name="credential">Used for a Microsoft Entra bearer token when no key is supplied.</param>
    public AzureOpenAIImageGenerator(
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
            $"{endpoint.TrimEnd('/')}/openai/deployments/{Uri.EscapeDataString(model)}"
                + $"/images/generations?api-version={ApiVersion}"
        );
    }

    public async Task<ImageGenerationResponse> GenerateAsync(
        ImageGenerationRequest request,
        ImageGenerationOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        var size = options?.ImageSize;

        // No response_format: gpt-image-1 answers base64 always and rejects the parameter with a 400.
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new ImageRequest(
                Model: _model,
                Prompt: request.Prompt ?? string.Empty,
                Size: $"{size?.Width ?? 1024}x{size?.Height ?? 1024}",
                Count: 1
            )
        );

        // Buffered, not JsonContent, for a declared Content-Length — same reason as the FLUX route.
        using var message = new HttpRequestMessage(HttpMethod.Post, _requestUri)
        {
            Content = new ByteArrayContent(payload)
            {
                Headers = { ContentType = new MediaTypeHeaderValue("application/json") },
            },
        };

        if (_apiKey is not null)
        {
            message.Headers.Add("api-key", _apiKey);
        }
        else
        {
            var token = await _credential!.GetTokenAsync(
                new TokenRequestContext([CognitiveServicesScope]),
                cancellationToken
            );
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        }

        using var response = await _http.SendAsync(message, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            // The status code has to survive on the exception: the renderer decides from it whether
            // the failure is worth retrying (429, 5xx) or permanent (404, 401, 400).
            throw new HttpRequestException(
                $"Image request to {_requestUri.AbsolutePath} failed with {(int)response.StatusCode} {response.StatusCode}: "
                    + Truncate(error, 500),
                inner: null,
                statusCode: response.StatusCode
            );
        }

        var body = await response.Content.ReadFromJsonAsync<ImageResponse>(cancellationToken)
            ?? throw new InvalidOperationException("The image deployment returned an empty response body.");

        var contents = new List<AIContent>();
        foreach (var item in body.Data ?? [])
        {
            if (!string.IsNullOrEmpty(item.B64Json))
            {
                contents.Add(new DataContent(Convert.FromBase64String(item.B64Json), "image/png"));
            }
            else if (!string.IsNullOrEmpty(item.Url))
            {
                // gpt-image-1 always inlines base64; dall-e-3 on the same route hands back a URL.
                contents.Add(new UriContent(new Uri(item.Url), "image/png"));
            }
        }

        return new ImageGenerationResponse(contents) { RawRepresentation = body };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";

    private sealed record ImageRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("size")] string Size,
        [property: JsonPropertyName("n")] int Count
    );

    private sealed record ImageResponse([property: JsonPropertyName("data")] List<Image>? Data);

    private sealed record Image(
        [property: JsonPropertyName("b64_json")] string? B64Json,
        [property: JsonPropertyName("url")] string? Url
    );
}
