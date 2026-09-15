using System.Net;
using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using StoryWalker.Application.Images;
using StoryWalker.Domain.Enums;
using StoryWalker.Infrastructure.Persistence;

namespace StoryWalker.Infrastructure.Generation;

/// <summary>
/// Renders one page's illustration and stores it (ADR-0010). Kept out of the triggered function so
/// it can be tested as an ordinary service against a real database (ADR-0017).
/// <para>
/// Retrying transient failures is this class's job, not the host's (ADR-0020). Throwing straight
/// out would abandon the Service Bus message, which becomes available again immediately — so a
/// throttled deployment would burn the queue's ten deliveries in seconds instead of waiting for the
/// throttle to clear. Backing off in-process holds the message lock, which the Service Bus
/// extension renews automatically.
/// </para>
/// </summary>
public sealed class PageImageRenderer(
    StoryWalkerDbContext db,
    IImageGenerator generator,
    IImageStore store,
    IHttpClientFactory httpClientFactory,
    ILogger<PageImageRenderer> logger
)
{
    private const int MaxAttempts = 3;

    /// <summary>
    /// Delay before the first retry; doubles on each subsequent attempt. Settable so tests do not
    /// have to sit through the real backoff.
    /// </summary>
    public TimeSpan FirstRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Generates the image for one page, uploads it, and records the URL. Idempotent (ADR-0005):
    /// a page that already has an image is skipped without calling the model, so a replayed
    /// activity costs nothing.
    /// </summary>
    /// <returns>True when the page ends up with an image.</returns>
    public async Task<bool> RenderAsync(Guid storyId, int pageNo, CancellationToken ct = default)
    {
        var page = await db
            .Pages.Include(p => p.Story)
            .FirstOrDefaultAsync(p => p.StoryId == storyId && p.PageNo == pageNo, ct);

        if (page is null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(page.ImageUrl))
        {
            return true;
        }

        var prompt = BuildPrompt(page.IllustrationPrompt, page.Story.ArtStyle, page.Story.TargetAge);

        ImageGenerationResponse response;
        try
        {
            response = await GenerateWithBackoffAsync(prompt, ct);
        }
        catch (Exception ex) when (!IsTransient(ex))
        {
            // Permanent: a 4xx from the model (wrong route or deployment, bad credentials, a
            // refused prompt) returns the same answer however many times it is asked. Failing the
            // page — instead of throwing — keeps Service Bus from redelivering the message ten times
            // and dead-lettering the story while it sits in Generating forever, which is exactly
            // what a 404 from the image route did on 2026-09-15.
            logger.LogError(
                ex,
                "Image generation for story {StoryId} page {PageNo} failed permanently; marking the page Failed.",
                storyId,
                pageNo
            );
            return await MarkFailedAsync(page.Id, ct);
        }

        // Storage and database failures below are deliberately NOT caught: they are infrastructure
        // hiccups, and a redelivered message resumes from this page because the pipeline is
        // idempotent.
        var (bytes, mediaType) = await ExtractImageAsync(response, ct);
        if (bytes is null || bytes.Value.IsEmpty)
        {
            logger.LogError(
                "Image generation for story {StoryId} page {PageNo} returned no image; marking the page Failed.",
                storyId,
                pageNo
            );
            return await MarkFailedAsync(page.Id, ct);
        }

        page.ImageUrl = await store.SaveAsync(storyId, pageNo, bytes.Value, mediaType, ct);
        page.Status = PageStatus.Complete;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Retries transient failures — throttling, a server-side error, a dropped connection — with
    /// exponential backoff. Anything else is rethrown on the first attempt so the caller can fail
    /// just this page: retrying a request that will get the same answer only wastes the budget.
    /// </summary>
    private async Task<ImageGenerationResponse> GenerateWithBackoffAsync(string prompt, CancellationToken ct)
    {
        var options = new ImageGenerationOptions
        {
            Count = 1,
            // Ask for bytes rather than a URL: a hosted image URL is short-lived, and we have to
            // re-upload the content to our own container anyway.
            ResponseFormat = ImageGenerationResponseFormat.Data,
        };

        var delay = FirstRetryDelay;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await generator.GenerateImagesAsync(prompt, options, ct);
            }
            catch (Exception ex) when (attempt < MaxAttempts && IsTransient(ex))
            {
                logger.LogWarning(
                    ex,
                    "Transient image generation failure (attempt {Attempt} of {MaxAttempts}); retrying in {Delay}.",
                    attempt,
                    MaxAttempts,
                    delay
                );
                await Task.Delay(delay, ct);
                delay *= 2;
            }
        }
    }

    /// <summary>
    /// The style suffix is composed here, in code, and is byte-identical across all six pages of a
    /// story. With no per-character reference image and no seed on the image API (ADR-0019), that
    /// identical trailing text — plus the character descriptions the brief repeats in every page
    /// prompt — is the entire consistency mechanism.
    /// </summary>
    internal static string BuildPrompt(string illustrationPrompt, string artStyle, int targetAge) =>
        $"{illustrationPrompt}\n\nArt style: {artStyle}. Children's picture-book illustration for a "
        + $"{targetAge}-year-old. No text, lettering or speech bubbles in the image.";

    private async Task<(ReadOnlyMemory<byte>? Bytes, string MediaType)> ExtractImageAsync(
        ImageGenerationResponse response,
        CancellationToken ct
    )
    {
        foreach (var content in response.Contents)
        {
            switch (content)
            {
                case DataContent data:
                    return (data.Data, data.MediaType ?? "image/png");

                // Some deployments ignore the requested format and hand back a short-lived URL; the
                // bytes still have to land in our own container, so fetch them.
                case UriContent uri:
                    using (var http = httpClientFactory.CreateClient())
                    {
                        var fetched = await http.GetByteArrayAsync(uri.Uri, ct);
                        return (fetched, uri.MediaType ?? "image/png");
                    }
            }
        }

        return (null, "image/png");
    }

    private async Task<bool> MarkFailedAsync(Guid pageId, CancellationToken ct)
    {
        var page = await db.Pages.FirstAsync(p => p.Id == pageId, ct);
        page.Status = PageStatus.Failed;
        await db.SaveChangesAsync(ct);
        return false;
    }

    /// <summary>
    /// Worth retrying: the request never got an HTTP answer, or the answer says "not now" —
    /// 408, 429, or any 5xx — or it timed out. Every other failure is permanent, including every
    /// other 4xx: a content-filter refusal, an unknown deployment and a rejected key all give the
    /// same result on every attempt.
    /// </summary>
    internal static bool IsTransient(Exception ex) =>
        ex switch
        {
            HttpRequestException { StatusCode: null } => true,
            HttpRequestException { StatusCode: var status } =>
                status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
                || (int)status >= 500,
            // HttpClient's own timeout surfaces as a cancellation; so does host shutdown, and in
            // that case the retry delay throws straight away, so nothing is retried.
            OperationCanceledException => true,
            _ => false,
        };
}
