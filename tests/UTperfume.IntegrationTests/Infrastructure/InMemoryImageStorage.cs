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

    public async Task UploadAsync(string blobPath, Stream content, string contentType, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        if (!_blobs.TryAdd(blobPath, (buffer.ToArray(), contentType)))
        {
            throw new InvalidOperationException($"Blob '{blobPath}' already exists.");
        }
    }

    public Task DeleteAsync(string blobPath, CancellationToken cancellationToken)
    {
        _blobs.TryRemove(blobPath, out _);
        return Task.CompletedTask;
    }

    public string GetPublicUrl(string blobPath) => ImagePublicUrl.Build(PublicBaseUrl, blobPath);
}
