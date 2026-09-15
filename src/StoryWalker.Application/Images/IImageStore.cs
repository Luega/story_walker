namespace StoryWalker.Application.Images;

/// <summary>
/// Stores a rendered page image and returns the URL to persist on the page (ADR-0010). The DB
/// never holds image bytes, only the URL.
/// <para>
/// The returned URL must be reachable by whoever will open it, which is not the same host the
/// worker uploaded from: in compose the worker writes to <c>http://azurite:10000</c> while a
/// browser needs <c>http://localhost:10000</c>. Implementations compose the URL from a configured
/// public base rather than echoing the SDK's own upload URI.
/// </para>
/// </summary>
public interface IImageStore
{
    /// <param name="storyId">Owning story; scopes the blob path so a story's images sit together.</param>
    /// <param name="pageNo">Page number; makes the path deterministic, so a retry overwrites rather than duplicates.</param>
    /// <param name="content">Image bytes. Deliberately not <c>BinaryData</c>: that type comes with the
    /// Azure SDKs, and this layer must not depend on them (ADR-0015).</param>
    /// <param name="contentType">Media type of <paramref name="content"/>, e.g. <c>image/png</c>.</param>
    /// <returns>A URL that resolves to the stored image.</returns>
    Task<string> SaveAsync(
        Guid storyId,
        int pageNo,
        ReadOnlyMemory<byte> content,
        string contentType,
        CancellationToken ct = default
    );
}
