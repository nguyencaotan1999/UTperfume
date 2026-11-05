using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;
using UTperfume.Application.Common;

namespace UTperfume.Infrastructure.Storage;

/// <summary>
/// Azure Blob implementation of <see cref="IImageStorage"/>. The client is created lazily, so the
/// application starts and tests run without a storage endpoint (Azurite) being reachable.
/// </summary>
public sealed class AzureBlobImageStorage : IImageStorage
{
    private const string ImmutableCacheControl = "public, max-age=31536000, immutable";

    private readonly ImageStorageOptions _options;
    private readonly Lazy<BlobContainerClient> _container;

    public AzureBlobImageStorage(IOptions<ImageStorageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _container = new Lazy<BlobContainerClient>(CreateContainerClient, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task UploadAsync(string blobPath, Stream content, string contentType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ValidatePath(blobPath);

        var blob = _container.Value.GetBlobClient(blobPath);
        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType, CacheControl = ImmutableCacheControl },
            // Names are unique GUIDs; refuse to overwrite an existing blob.
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
        };
        await blob.UploadAsync(content, options, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string blobPath, CancellationToken cancellationToken)
    {
        ValidatePath(blobPath);
        await _container.Value.GetBlobClient(blobPath)
            .DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public string GetPublicUrl(string blobPath) =>
        ImagePublicUrl.Build(_options.PublicBaseUrl!, blobPath);

    /// <summary>Creates the container (anonymous blob read, like production) if it does not exist. Development use.</summary>
    public async Task EnsureContainerExistsAsync(CancellationToken cancellationToken) =>
        await _container.Value.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

    private static void ValidatePath(string blobPath)
    {
        // Reuses the same rules as URL building so a path that cannot be served cannot be stored.
        _ = ImagePublicUrl.Build("http://localhost", blobPath);
    }

    private BlobContainerClient CreateContainerClient()
    {
        // A connection string is used for local Azurite only. In Azure, Managed Identity is used.
        BlobServiceClient service = !string.IsNullOrWhiteSpace(_options.ConnectionString)
            ? new BlobServiceClient(_options.ConnectionString)
            : new BlobServiceClient(new Uri(_options.ServiceUri!), new DefaultAzureCredential());

        return service.GetBlobContainerClient(_options.Container);
    }
}
