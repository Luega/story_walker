using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using StoryWalker.Application.Images;

namespace StoryWalker.Infrastructure.Images;

/// <summary>
/// Blob Storage implementation of <see cref="IImageStore"/> (ADR-0010). Locally this is the
/// azurite service already running for the Durable task hub — same account, different container.
/// </summary>
/// <param name="publicBaseUrl">
/// Base the returned URL is composed from, e.g. <c>http://localhost:10000/devstoreaccount1</c>.
/// Deliberately not derived from <c>BlobClient.Uri</c>: inside the compose network the worker
/// reaches azurite as <c>http://azurite:10000</c>, a host that does not resolve from the browser
/// the URL is eventually opened in, so echoing the upload URI stores links that 404.
/// </param>
internal sealed class BlobImageStore(BlobServiceClient client, string containerName, string publicBaseUrl)
    : IImageStore
{
    private readonly string _publicBaseUrl = publicBaseUrl.TrimEnd('/');

    public async Task<string> SaveAsync(
        Guid storyId,
        int pageNo,
        ReadOnlyMemory<byte> content,
        string contentType,
        CancellationToken ct = default
    )
    {
        var container = client.GetBlobContainerClient(containerName);

        // Blob-level public access: a SAS stored in the DB would expire and rot, and minting one at
        // read time would mean a second code path in the API just to serve a picture.
        await container.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: ct);

        // Deterministic path: a retried render overwrites its own blob instead of leaving a duplicate.
        var path = BlobPath(storyId, pageNo, contentType);

        await container
            .GetBlobClient(path)
            .UploadAsync(
                BinaryData.FromBytes(content),
                new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
                ct
            );

        return $"{_publicBaseUrl}/{containerName}/{path}";
    }

    private static string BlobPath(Guid storyId, int pageNo, string contentType) =>
        $"stories/{storyId}/pages/{pageNo}{ExtensionFor(contentType)}";

    private static string ExtensionFor(string contentType) =>
        contentType switch
        {
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            _ => ".png",
        };
}
