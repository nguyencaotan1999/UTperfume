namespace UTperfume.Application.Common;

/// <summary>
/// Stores catalogue images. The database holds only the relative blob path; public URLs are built
/// from configuration so a CDN can be added without data changes (ADR-0014).
/// </summary>
public interface IImageStorage
{
    /// <summary>Uploads a new blob. Never overwrites: an existing path is an error.</summary>
    /// <param name="blobPath">Relative, server-generated path such as <c>products/12/{guid}.jpg</c>.</param>
    Task UploadAsync(string blobPath, Stream content, string contentType, CancellationToken cancellationToken);

    /// <summary>Deletes a blob. Deleting a blob that does not exist is not an error.</summary>
    Task DeleteAsync(string blobPath, CancellationToken cancellationToken);

    /// <summary>Builds the public URL of a blob from the configured base URL and the relative path.</summary>
    string GetPublicUrl(string blobPath);
}
