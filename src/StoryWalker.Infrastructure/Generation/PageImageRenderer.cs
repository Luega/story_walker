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
    IllustrationPromptRewriter rewriter,
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
    /// <returns>How the page ended; only <see cref="PageRenderOutcome.Rendered"/> has an image.</returns>
    public async Task<PageRenderOutcome> RenderAsync(Guid storyId, int pageNo, CancellationToken ct = default)
    {
        var page = await db
            .Pages.Include(p => p.Story)
            .FirstOrDefaultAsync(p => p.StoryId == storyId && p.PageNo == pageNo, ct);

        if (page is null)
        {
            return PageRenderOutcome.Failed;
        }

        if (!string.IsNullOrWhiteSpace(page.ImageUrl))
        {
            return PageRenderOutcome.Rendered;
        }

        var prompt = BuildPrompt(page.IllustrationPrompt, page.Story.ArtStyle, page.Story.TargetAge);

        ImageGenerationResponse response;
        try
        {
            response = await GenerateWithBackoffAsync(prompt, ct);
        }
        catch (Exception ex) when (IsModerationRefusal(ex))
        {
            // The same prompt would be refused again, but the story is usually fine and only the
            // staging of the scene tripped the classifier. Reword this page's illustration prompt,
            // never its text, and try once more (ADR-0025).
            logger.LogWarning(
                ex,
                "Image generation for story {StoryId} page {PageNo} was refused on moderation grounds; rewriting its illustration prompt once.",
                storyId,
                pageNo
            );

            var rewritten = await rewriter.RewriteAsync(
                page.IllustrationPrompt,
                page.Content,
                page.Story.TargetAge,
                ct
            );
            if (rewritten is null)
            {
                logger.LogError(
                    "No usable rewrite for story {StoryId} page {PageNo}; marking the page Failed.",
                    storyId,
                    pageNo
                );
                return await MarkFailedAsync(page.Id, PageRenderOutcome.Refused, ct);
            }

            // Saved with the page whichever way the second attempt goes, so the database records
            // the prompt that was actually sent.
            page.IllustrationPrompt = rewritten;

            try
            {
                response = await GenerateWithBackoffAsync(
                    BuildPrompt(rewritten, page.Story.ArtStyle, page.Story.TargetAge),
                    ct
                );
            }
            catch (Exception retryEx) when (!IsTransient(retryEx))
            {
                logger.LogError(
                    retryEx,
                    "Image generation for story {StoryId} page {PageNo} failed again with the rewritten prompt; marking the page Failed.",
                    storyId,
                    pageNo
                );
                return await MarkFailedAsync(
                    page.Id,
                    IsModerationRefusal(retryEx) ? PageRenderOutcome.Refused : PageRenderOutcome.Failed,
                    ct
                );
            }
        }
        catch (Exception ex) when (!IsTransient(ex))
        {
            // Permanent: a 4xx from the model (wrong route or deployment, bad credentials) returns
            // the same answer however many times it is asked. Failing the
            // page — instead of throwing — keeps Service Bus from redelivering the message ten times
            // and dead-lettering the story while it sits in Generating forever, which is exactly
            // what a 404 from the image route did on 2026-09-15.
            logger.LogError(
                ex,
                "Image generation for story {StoryId} page {PageNo} failed permanently; marking the page Failed.",
                storyId,
                pageNo
            );
            return await MarkFailedAsync(page.Id, PageRenderOutcome.Failed, ct);
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
            return await MarkFailedAsync(page.Id, PageRenderOutcome.Failed, ct);
        }

        page.ImageUrl = await store.SaveAsync(storyId, pageNo, bytes.Value, mediaType, ct);
        page.Status = PageStatus.Complete;
        await db.SaveChangesAsync(ct);
        return PageRenderOutcome.Rendered;
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

    private async Task<PageRenderOutcome> MarkFailedAsync(
        Guid pageId,
        PageRenderOutcome outcome,
        CancellationToken ct
    )
    {
        var page = await db.Pages.FirstAsync(p => p.Id == pageId, ct);
        page.Status = PageStatus.Failed;
        await db.SaveChangesAsync(ct);
        return outcome;
    }

    /// <summary>
    /// The error codes image providers use when their safety system refuses a prompt:
    /// <c>moderation_blocked</c> (gpt-image), <c>content_policy_violation</c> (dall-e), and Azure's
    /// own content filter. Matched in the message, where the image generators put the response body.
    /// </summary>
    private static readonly string[] ModerationCodes =
    [
        "moderation_blocked",
        "content_policy_violation",
        "content_filter",
        "ResponsibleAIPolicyViolation",
    ];

    /// <summary>
    /// A refusal by the image model's safety system (ADR-0025): a 400, or an error with no HTTP
    /// status, that names one of <see cref="ModerationCodes"/>. Other permanent failures, such as a
    /// wrong route or a rejected key, have nothing to do with the prompt's wording, so rewriting it
    /// would change nothing.
    /// </summary>
    internal static bool IsModerationRefusal(Exception ex) =>
        ex is not HttpRequestException { StatusCode: not null and not HttpStatusCode.BadRequest }
        && ModerationCodes.Any(code => ex.Message.Contains(code, StringComparison.OrdinalIgnoreCase));

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
