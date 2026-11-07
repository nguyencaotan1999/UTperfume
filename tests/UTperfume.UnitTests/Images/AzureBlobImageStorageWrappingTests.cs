using Azure.Core.Pipeline;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;
using UTperfume.Application.Common;
using UTperfume.Infrastructure.Storage;

namespace UTperfume.UnitTests.Images;

/// <summary>
/// The adapter wraps transport failures in <see cref="ImageStorageUnavailableException"/>. The real Azure client is used with
/// a fake HTTP transport, so no Azurite or Azure account is needed (this is a plain unit test, never skipped).
/// </summary>
public sealed class AzureBlobImageStorageWrappingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AzureBlobImageStorage StorageFailingWith(Func<HttpResponseMessage> respond)
    {
        var options = new BlobClientOptions { Transport = new HttpClientTransport(new HttpClient(new StubHandler(respond))) };
        options.Retry.MaxRetries = 0;
        var container = new BlobContainerClient(new Uri("https://storage.invalid/catalog-images"), options);
        return new AzureBlobImageStorage(
            Options.Create(new ImageStorageOptions { PublicBaseUrl = "https://storage.invalid/catalog-images" }), container);
    }

    private static Task UploadAsync(AzureBlobImageStorage storage) =>
        storage.UploadAsync("products/1/abc.png", new MemoryStream([1, 2, 3]), "image/png", Ct);

    [Fact]
    public async Task EC_A_A503FromTheStorageService_IsWrappedAsStorageUnavailable()
    {
        var storage = StorageFailingWith(() => new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));

        var act = () => UploadAsync(storage);

        var thrown = await act.Should().ThrowAsync<ImageStorageUnavailableException>();
        thrown.Which.InnerException.Should().BeOfType<Azure.RequestFailedException>().Which.Status.Should().Be(503);
    }

    [Fact]
    public async Task EC_A_AnHttpRequestException_FromTheTransport_IsWrappedAsStorageUnavailable()
    {
        var storage = StorageFailingWith(() => throw new HttpRequestException("connection refused"));

        var act = () => UploadAsync(storage);

        await act.Should().ThrowAsync<ImageStorageUnavailableException>();
    }

    [Fact]
    public async Task EC_A_A4xxFromTheStorageService_IsNotWrapped()
    {
        var storage = StorageFailingWith(() => new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden));

        var act = () => UploadAsync(storage);

        await act.Should().ThrowAsync<Azure.RequestFailedException>();
    }

    private sealed class StubHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond());
    }
}
