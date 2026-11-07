using System.Collections.Concurrent;
using UTperfume.Application.Common;
using UTperfume.Infrastructure.Storage;

namespace UTperfume.IntegrationTests.Infrastructure;

/// <summary>Fake <see cref="IImageStorage"/>: no Azure or Azurite is needed to run the tests.</summary>
public sealed class InMemoryImageStorage : IImageStorage
{
    public const string PublicBaseUrl = "https://images.test/catalog-images";

    private readonly ConcurrentDictionary<string, (byte[] Content, string ContentType)> _blobs = new();

    public IReadOnlyCollection<string> Paths => [.. _blobs.Keys];

    /// <summary>When true, <see cref="DeleteAsync"/> throws (simulates a storage outage during a blob delete).</summary>
    public bool FailDeletes { get; set; }

    /// <summary>When true, <see cref="UploadAsync"/> throws the storage-unavailable exception the real adapter raises on an outage.</summary>
    public bool FailUploads { get; set; }

    public (byte[] Content, string ContentType)? Get(string blobPath) =>
        _blobs.TryGetValue(blobPath, out var blob) ? blob : null;

    public async Task UploadAsync(string blobPath, Stream content, string contentType, CancellationToken cancellationToken)
    {
        if (FailUploads)
        {
            throw new ImageStorageUnavailableException(
                "Simulated outage", new HttpRequestException("SDK-DETAIL https://account.blob.core.windows.net secret-storage-text"));
        }

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        if (!_blobs.TryAdd(blobPath, (buffer.ToArray(), contentType)))
        {
            throw new InvalidOperationException($"Blob '{blobPath}' already exists.");
        }
    }

    public Task DeleteAsync(string blobPath, CancellationToken cancellationToken)
    {
        if (FailDeletes)
        {
            throw new IOException("Simulated storage failure.");
        }

        _blobs.TryRemove(blobPath, out _);
        return Task.CompletedTask;
    }

    public string GetPublicUrl(string blobPath) => ImagePublicUrl.Build(PublicBaseUrl, blobPath);
}
