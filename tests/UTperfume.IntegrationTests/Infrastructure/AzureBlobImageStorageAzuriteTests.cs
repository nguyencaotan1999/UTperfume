using System.Net;
using System.Net.Sockets;
using Azure;
using Microsoft.Extensions.Options;
using UTperfume.Infrastructure.Storage;

namespace UTperfume.IntegrationTests.Infrastructure;

/// <summary>
/// Round trip against a locally running Azurite (<c>npx azurite --location .azurite --silent --skipApiVersionCheck</c>).
/// The tests are skipped when Azurite is not listening, so <c>dotnet test</c> never needs it.
/// </summary>
public sealed class AzureBlobImageStorageAzuriteTests
{
    private static bool AzuriteIsListening()
    {
        try
        {
            using var client = new TcpClient();
            return client.ConnectAsync(IPAddress.Loopback, 10000).Wait(TimeSpan.FromSeconds(1));
        }
        catch (Exception ex) when (ex is SocketException or AggregateException)
        {
            return false;
        }
    }

    [Fact]
    public async Task Upload_then_public_read_then_delete_round_trip()
    {
        Assert.SkipUnless(AzuriteIsListening(), "Azurite is not running on 127.0.0.1:10000.");
        var ct = TestContext.Current.CancellationToken;

        var container = "it-" + Guid.NewGuid().ToString("N")[..16];
        var options = new ImageStorageOptions
        {
            ConnectionString = "UseDevelopmentStorage=true",
            Container = container,
            PublicBaseUrl = $"http://127.0.0.1:10000/devstoreaccount1/{container}",
        };
        var storage = new AzureBlobImageStorage(Options.Create(options));
        await storage.EnsureContainerExistsAsync(ct);

        var path = $"products/1/{Guid.NewGuid():N}.png";
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
        await storage.UploadAsync(path, new MemoryStream(bytes), "image/png", ct);

        using var http = new HttpClient();
        var response = await http.GetAsync(storage.GetPublicUrl(path), ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("image/png");
        response.Headers.CacheControl?.ToString().Should().Contain("immutable");
        (await response.Content.ReadAsByteArrayAsync(ct)).Should().Equal(bytes);

        var overwrite = async () => await storage.UploadAsync(path, new MemoryStream(bytes), "image/png", ct);
        await overwrite.Should().ThrowAsync<RequestFailedException>();

        await storage.DeleteAsync(path, ct);
        await storage.DeleteAsync(path, ct); // idempotent
        (await http.GetAsync(storage.GetPublicUrl(path), ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
