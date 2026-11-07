using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using UTperfume.Application.Common;
using UTperfume.Application.Domain;
using UTperfume.Infrastructure.Persistence;
using UTperfume.IntegrationTests.Infrastructure;

namespace UTperfume.IntegrationTests.Api;

/// <summary>
/// P4-06: product image upload and delete through the real pipeline (authorization, antiforgery, multipart binding,
/// upload service, product row lock, audit stamping) on the per-run SQL Server database. Storage is the in-memory fake
/// (<see cref="InMemoryImageStorage"/>); no Azurite or Azure account is needed.
/// Covers BR-A-15, BR-A-22 to BR-A-31, AC-A-30 to AC-A-36 and AC-A10 of the catalogue requirements.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AdminProductImageTests : IAsyncLifetime, IDisposable
{
    private const string AdminUser = "42";
    private const int Max = 5_242_880;

    private readonly TestDatabase _database;
    private readonly FailureSwitch _failSave = new();
    private readonly ApiFactory _factory;
    private HttpClient _client = null!;
    private string _xsrf = string.Empty;

    public AdminProductImageTests(TestDatabase database)
    {
        _database = database;
        _factory = new ApiFactory(database, configureServices: services =>
        {
            // Lets a test make the database step of an upload fail after the blob was written (AC-A-36).
            services.AddSingleton(_failSave);
            services.RemoveAll<IAdminWriteSession>();
            services.AddScoped<IAdminWriteSession>(sp => new FailableSession(
                ActivatorUtilities.CreateInstance<AdminWriteSession>(sp), sp.GetRequiredService<FailureSwitch>()));
        });
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private InMemoryImageStorage Storage => _factory.ImageStorage;

    public async ValueTask InitializeAsync()
    {
        await _database.ResetCatalogAsync();
        _client = _factory.CreateSecureClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/auth/antiforgery");
        AsAdmin(request);
        using var response = await _client.SendAsync(request, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _xsrf = AdminIdentityTestSupport.CookieValue(response.SetCookies("__Host-utp-xsrf").Should().ContainSingle().Subject);
    }

    public ValueTask DisposeAsync()
    {
        _client?.Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose() => _factory.Dispose();

    // ---- helpers --------------------------------------------------------------------------------------------------

    private static void AsAdmin(HttpRequestMessage request, string roles = "Catalog.Admin")
    {
        request.Headers.Add(TestAuthHandler.UserHeader, AdminUser);
        request.Headers.Add(TestAuthHandler.RolesHeader, roles);
    }

    private static byte[] Pad(byte[] head, int length)
    {
        var bytes = new byte[Math.Max(length, head.Length)];
        head.CopyTo(bytes, 0);
        return bytes;
    }

    private static byte[] Jpeg(int length = 200) => Pad([0xFF, 0xD8, 0xFF, 0xE0], length);

    private static byte[] Png(int length = 200) => Pad([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], length);

    private static byte[] WebP(int length = 200) => Pad([.. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8], length);

    private async Task<int> SeedProductAsync(
        string slug = "p", ProductStatus status = ProductStatus.Draft, int images = 0, bool withVariant = false)
    {
        await using var db = _database.CreateContext();
        var brand = new Brand { Name = "b-" + slug, Slug = "b-" + slug };
        var product = new Product
        {
            Brand = brand, ConcentrationId = 3, Name = slug, Slug = slug, Status = status,
            PublishedAtUtc = status == ProductStatus.Draft ? null : DateTime.UtcNow.AddDays(-1),
        };
        if (withVariant)
        {
            product.Variants.Add(new ProductVariant { Sku = "SKU-" + slug.ToUpperInvariant(), VolumeMl = 50, Price = 1000, StockQuantity = 5 });
        }

        for (var i = 0; i < images; i++)
        {
            product.Images.Add(new ProductImage
            {
                BlobPath = $"products/seed/{slug}-{i}.jpg", ContentType = "image/jpeg", SizeBytes = 10, SortOrder = i, IsPrimary = i == 0,
            });
        }

        db.Add(product);
        await db.SaveChangesAsync(Ct);
        return product.Id;
    }

    private HttpRequestMessage UploadRequest(
        int productId, byte[]? file, string contentType = "image/jpeg", string fileName = "photo.jpg",
        string? altText = null, bool? isPrimary = null, string? xsrf = "valid", bool authenticate = true, string roles = "Catalog.Admin")
    {
        var form = new MultipartFormDataContent();
        if (file is not null)
        {
            var part = new ByteArrayContent(file);
            if (contentType.Length > 0)
            {
                part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            }

            form.Add(part, "file", fileName);
        }

        if (altText is not null)
        {
            form.Add(new StringContent(altText), "altText");
        }

        if (isPrimary is { } primary)
        {
            form.Add(new StringContent(primary ? "true" : "false"), "isPrimary");
        }

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/products/{productId}/images") { Content = form };
        if (authenticate)
        {
            AsAdmin(request, roles);
        }

        if (xsrf is not null)
        {
            request.Headers.Add("X-XSRF-TOKEN", xsrf == "valid" ? _xsrf : xsrf);
        }

        return request;
    }

    private HttpRequestMessage DeleteRequest(int productId, int imageId, string? xsrf = "valid", bool authenticate = true, string roles = "Catalog.Admin")
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/admin/products/{productId}/images/{imageId}");
        if (authenticate)
        {
            AsAdmin(request, roles);
        }

        if (xsrf is not null)
        {
            request.Headers.Add("X-XSRF-TOKEN", xsrf == "valid" ? _xsrf : xsrf);
        }

        return request;
    }

    private async Task<(HttpResponseMessage Response, JsonElement Json, string Text)> SendAsync(HttpRequestMessage request)
    {
        var response = await _client.SendAsync(request, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        var json = string.IsNullOrEmpty(text) || !text.TrimStart().StartsWith('{') ? default : JsonDocument.Parse(text).RootElement.Clone();
        return (response, json, text);
    }

    private async Task<(HttpResponseMessage Response, JsonElement Json, string Text)> UploadAsync(
        int productId, byte[] file, string contentType = "image/jpeg", string fileName = "photo.jpg", string? altText = null, bool? isPrimary = null) =>
        await SendAsync(UploadRequest(productId, file, contentType, fileName, altText, isPrimary));

    private async Task<List<ProductImage>> ImagesAsync(int productId)
    {
        await using var db = _database.CreateContext();
        return await db.ProductImages.AsNoTracking().Where(i => i.ProductId == productId).OrderBy(i => i.SortOrder).ThenBy(i => i.Id).ToListAsync(Ct);
    }

    private async Task<int> TotalImageRowsAsync()
    {
        await using var db = _database.CreateContext();
        return await db.ProductImages.CountAsync(Ct);
    }

    private static string Code(JsonElement json) => json.GetProperty("code").GetString()!;

    private List<CapturedLog> AuditEvents() =>
        [.. _factory.Logs.Entries.Where(e => e.Message.StartsWith("Admin audit:", StringComparison.Ordinal))];

    // ---- AC-A-30: the happy path ----------------------------------------------------------------------------------

    [Fact]
    public async Task AC_A_30_AValidWebpUpload_ToAProductWithoutImages_Returns201_Primary_WithAGeneratedPathAndAPublicUrl()
    {
        var productId = await SeedProductAsync();
        var content = WebP(1_000_000);

        var (response, json, text) = await UploadAsync(productId, content, "image/webp", "bottle.webp", "A bottle");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        json.GetProperty("isPrimary").GetBoolean().Should().BeTrue();
        json.GetProperty("sortOrder").GetInt32().Should().Be(0);
        json.GetProperty("altText").GetString().Should().Be("A bottle");
        json.GetProperty("contentType").GetString().Should().Be("image/webp");
        json.GetProperty("sizeBytes").GetInt32().Should().Be(1_000_000);
        json.GetProperty("originalFileName").GetString().Should().Be("bottle.webp");
        json.GetProperty("url").GetString().Should().StartWith(InMemoryImageStorage.PublicBaseUrl + "/");
        text.Should().NotContainEquivalentOf("blobPath", "no admin DTO contains the blob path (BR-A-22)");

        var row = (await ImagesAsync(productId)).Single();
        row.BlobPath.Should().MatchRegex($"^products/{productId}/[0-9a-f]{{32}}\\.webp$");
        json.GetProperty("url").GetString().Should().Be($"{InMemoryImageStorage.PublicBaseUrl}/{row.BlobPath}");
        Storage.Paths.Should().ContainSingle().Which.Should().Be(row.BlobPath);
        Storage.Get(row.BlobPath)!.Value.ContentType.Should().Be("image/webp");
        Storage.Get(row.BlobPath)!.Value.Content.Should().Equal(content, "the file is stored as uploaded, not re-encoded");
        response.Headers.Location!.ToString().Should().Be($"/api/v1/admin/products/{productId}/images/{row.Id}");
    }

    [Fact]
    public async Task BR_A_22_AdminImageResponses_AreNoStore()
    {
        var productId = await SeedProductAsync();

        var (response, _, _) = await UploadAsync(productId, Jpeg());

        (response.Headers.CacheControl?.ToString() ?? string.Empty).Should().Be("no-store");
    }

    [Theory]
    [InlineData("image/jpeg", "photo.jpg", "jpg")]
    [InlineData("image/png", "photo.png", "png")]
    [InlineData("image/webp", "photo.webp", "webp")]
    public async Task BR_A_23_EachAcceptedType_IsStoredWithItsDetectedContentTypeAndExtension(string contentType, string fileName, string extension)
    {
        var productId = await SeedProductAsync();
        byte[] content = extension switch { "png" => Png(), "webp" => WebP(), _ => Jpeg() };

        var (response, _, _) = await UploadAsync(productId, content, contentType, fileName);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var row = (await ImagesAsync(productId)).Single();
        row.BlobPath.Should().EndWith("." + extension);
        row.ContentType.Should().Be(contentType);
    }

    [Fact]
    public async Task BR_A_30_WidthAndHeight_AreRecordedWhenTheHeaderShowsThem_AndNullOtherwise()
    {
        var productId = await SeedProductAsync();
        byte[] png =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, .. "IHDR"u8, 0, 0, 0x03, 0x20, 0, 0, 0x02, 0x58, 8, 6, 0, 0, 0,
        ];

        var (_, withSize, _) = await UploadAsync(productId, png, "image/png", "a.png");
        var (_, withoutSize, _) = await UploadAsync(productId, Jpeg(), "image/jpeg", "b.jpg");

        withSize.GetProperty("width").GetInt32().Should().Be(800);
        withSize.GetProperty("height").GetInt32().Should().Be(600);
        withoutSize.GetProperty("width").ValueKind.Should().Be(JsonValueKind.Null);
        withoutSize.GetProperty("height").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ---- AC-A10 / AC-A-31 / AC-A-32: validation, nothing stored on failure ------------------------------------------

    [Theory]
    [InlineData("svg", "image/svg+xml")]
    [InlineData("svg", "image/jpeg")]
    [InlineData("gif", "image/gif")]
    [InlineData("gif", "image/jpeg")]
    [InlineData("png", "image/jpeg")]
    [InlineData("jpeg", "image/png")]
    [InlineData("jpeg", "image/webp")]
    [InlineData("jpeg", "text/plain")]
    [InlineData("jpeg", "")]
    public async Task AC_A_31_AnUnsupportedTypeOrMismatchedSignature_Returns415_AndStoresNothing(string actual, string declared)
    {
        var productId = await SeedProductAsync();
        byte[] content = actual switch
        {
            "svg" => System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"),
            "gif" => System.Text.Encoding.ASCII.GetBytes("GIF89a\x01\0\x01\0\0\0\0"),
            "png" => Png(),
            _ => Jpeg(),
        };

        var (response, json, _) = await UploadAsync(productId, content, declared, "x.bin");

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        Code(json).Should().Be("unsupported_image_type");
        Storage.Paths.Should().BeEmpty();
        (await TotalImageRowsAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AC_A_32_AFileOfExactlyFiveMegabytes_Returns201()
    {
        var productId = await SeedProductAsync();

        var (response, json, _) = await UploadAsync(productId, Jpeg(Max));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        json.GetProperty("sizeBytes").GetInt32().Should().Be(Max);
        Storage.Paths.Should().HaveCount(1);
    }

    [Fact]
    public async Task AC_A_32_AFileOneByteOverFiveMegabytes_Returns413_AndStoresNothing()
    {
        var productId = await SeedProductAsync();

        var (response, json, _) = await UploadAsync(productId, Jpeg(Max + 1));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        Code(json).Should().Be("image_too_large");
        Storage.Paths.Should().BeEmpty();
        (await TotalImageRowsAsync()).Should().Be(0);
    }

    [Fact]
    public async Task BR_A_24_AnEmptyFile_Returns400OnTheFileField_AndStoresNothing()
    {
        var productId = await SeedProductAsync();

        var (response, json, _) = await UploadAsync(productId, []);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        json.GetProperty("errors").TryGetProperty("file", out _).Should().BeTrue();
        Storage.Paths.Should().BeEmpty();
        (await TotalImageRowsAsync()).Should().Be(0);
    }

    [Fact]
    public async Task BR_A_24_ARequestWithoutAFilePart_Returns400OnTheFileField_AndStoresNothing()
    {
        var productId = await SeedProductAsync();

        var (response, json, _) = await SendAsync(UploadRequest(productId, file: null, altText: "only text"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        json.GetProperty("errors").TryGetProperty("file", out _).Should().BeTrue();
        Storage.Paths.Should().BeEmpty();
    }

    [Fact]
    public async Task BR_A_24_ARequestWithTwoFileParts_Returns400OnTheFileField_AndStoresNothing()
    {
        var productId = await SeedProductAsync();
        var request = UploadRequest(productId, Jpeg());
        ((MultipartFormDataContent)request.Content!).Add(new ByteArrayContent(Jpeg()) { Headers = { ContentType = new("image/jpeg") } }, "file", "second.jpg");

        var (response, json, _) = await SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        json.GetProperty("errors").TryGetProperty("file", out _).Should().BeTrue();
        Storage.Paths.Should().BeEmpty();
        (await TotalImageRowsAsync()).Should().Be(0);
    }

    [Fact]
    public async Task BR_A_28_AnIsPrimaryThatIsNotABoolean_Returns400OnTheIsPrimaryField_AndStoresNothing()
    {
        var productId = await SeedProductAsync();
        var request = UploadRequest(productId, Jpeg());
        ((MultipartFormDataContent)request.Content!).Add(new StringContent("yes"), "isPrimary");

        var (response, json, _) = await SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        json.GetProperty("errors").TryGetProperty("isPrimary", out _).Should().BeTrue();
        Storage.Paths.Should().BeEmpty();
        (await TotalImageRowsAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("True", true)]
    [InlineData("TRUE", true)]
    [InlineData("FALSE", false)]
    [InlineData("False", false)]
    public async Task BR_A_28_AMixedCaseBoolean_IsAccepted(string value, bool expectedPrimary)
    {
        var productId = await SeedProductAsync(images: 1);
        var request = UploadRequest(productId, Jpeg());
        ((MultipartFormDataContent)request.Content!).Add(new StringContent(value), "isPrimary");

        var (response, json, _) = await SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        json.GetProperty("isPrimary").GetBoolean().Should().Be(expectedPrimary);
    }

    [Fact]
    public async Task BR_A_28_AnEmptyIsPrimary_IsTreatedAsMissing()
    {
        var productId = await SeedProductAsync(images: 1);
        var request = UploadRequest(productId, Jpeg());
        ((MultipartFormDataContent)request.Content!).Add(new StringContent(string.Empty), "isPrimary");

        var (response, json, _) = await SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        json.GetProperty("isPrimary").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task BR_A_29_AnAltTextOverTwoHundredFiftyCharacters_Returns400_AndStoresNothing_WhileExactly250IsAccepted()
    {
        var productId = await SeedProductAsync();

        var (tooLong, json, _) = await UploadAsync(productId, Jpeg(), altText: new string('a', 251));
        var (exact, _, _) = await UploadAsync(productId, Jpeg(), altText: new string('b', 250));

        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        json.GetProperty("errors").TryGetProperty("altText", out _).Should().BeTrue();
        exact.StatusCode.Should().Be(HttpStatusCode.Created);
        Storage.Paths.Should().HaveCount(1, "only the accepted upload stored a blob");
    }

    [Fact]
    public async Task BR_A_29_ABlankAltText_IsStoredAsNull()
    {
        var productId = await SeedProductAsync();

        var (_, json, _) = await UploadAsync(productId, Jpeg(), altText: "   ");

        json.GetProperty("altText").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task AC_A_35_AMaliciousFileName_NeverReachesTheBlobPath_AndIsReturnedOnlyAsData()
    {
        var productId = await SeedProductAsync();

        var (response, json, _) = await UploadAsync(productId, Jpeg(), "image/jpeg", "../../evil.jpg");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var row = (await ImagesAsync(productId)).Single();
        row.BlobPath.Should().NotContain("evil").And.NotContain("..");
        json.GetProperty("url").GetString().Should().NotContain("evil");
        json.GetProperty("originalFileName").GetString().Should().Contain("evil.jpg");
    }

    [Fact]
    public async Task BR_A_26_ALongFileName_IsTruncatedTo255Characters()
    {
        var productId = await SeedProductAsync();

        var (response, json, _) = await UploadAsync(productId, Jpeg(), "image/jpeg", new string('n', 300) + ".jpg");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        json.GetProperty("originalFileName").GetString()!.Length.Should().Be(255);
    }

    [Fact]
    public async Task BR_A_24_AJsonBodyInsteadOfAMultipartForm_IsRejectedWithoutStoringAnything()
    {
        var productId = await SeedProductAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/products/{productId}/images")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        };
        AsAdmin(request);
        request.Headers.Add("X-XSRF-TOKEN", _xsrf);

        var (response, _, _) = await SendAsync(request);

        ((int)response.StatusCode).Should().BeInRange(400, 499);
        Storage.Paths.Should().BeEmpty();
    }

    [Fact]
    public async Task BR_A_10_AnUnknownProduct_Returns404_AndStoresNothing()
    {
        var (response, _, _) = await UploadAsync(987654, Jpeg());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Storage.Paths.Should().BeEmpty();
    }

    // ---- BR-A-28: primary and order -------------------------------------------------------------------------------

    [Fact]
    public async Task BR_A_28_TheFirstImageIsPrimary_AndLaterImagesAreAppendedAfterTheMaximumSortOrder()
    {
        var productId = await SeedProductAsync();

        var (_, first, _) = await UploadAsync(productId, Jpeg());
        var (_, second, _) = await UploadAsync(productId, Jpeg());
        var (_, third, _) = await UploadAsync(productId, Jpeg());

        first.GetProperty("isPrimary").GetBoolean().Should().BeTrue();
        second.GetProperty("isPrimary").GetBoolean().Should().BeFalse();
        third.GetProperty("isPrimary").GetBoolean().Should().BeFalse();
        new[] { first, second, third }.Select(j => j.GetProperty("sortOrder").GetInt32()).Should().Equal(0, 1, 2);
    }

    [Fact]
    public async Task BR_A_28_UploadingWithIsPrimaryTrue_MakesTheNewImagePrimary_AndClearsTheOldOne()
    {
        var productId = await SeedProductAsync(images: 2);

        var (response, json, _) = await UploadAsync(productId, Jpeg(), isPrimary: true);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        json.GetProperty("isPrimary").GetBoolean().Should().BeTrue();
        var rows = await ImagesAsync(productId);
        rows.Should().HaveCount(3);
        rows.Where(r => r.IsPrimary).Should().ContainSingle().Which.Id.Should().Be(json.GetProperty("id").GetInt32());
        json.GetProperty("sortOrder").GetInt32().Should().Be(2);
    }

    // ---- AC-A-33: at most 6 images --------------------------------------------------------------------------------

    [Fact]
    public async Task AC_A_33_AProductWithFiveImages_AcceptsASixth_AndRejectsASeventh_With422_AndStoresNothing()
    {
        var productId = await SeedProductAsync(images: 5);

        var (sixth, _, _) = await UploadAsync(productId, Jpeg());
        var pathsAfterSixth = Storage.Paths.ToList();
        var (seventh, json, _) = await UploadAsync(productId, Jpeg());

        sixth.StatusCode.Should().Be(HttpStatusCode.Created);
        seventh.StatusCode.Should().Be((HttpStatusCode)422);
        Code(json).Should().Be("image_limit_reached");
        (await ImagesAsync(productId)).Should().HaveCount(6);
        Storage.Paths.Should().BeEquivalentTo(pathsAfterSixth, "the rejected upload wrote no blob");
        pathsAfterSixth.Should().HaveCount(1);
    }

    [Fact]
    public async Task AC_A_33_TwoConcurrentUploads_ToAProductWithFiveImages_LetExactlyOneSucceed()
    {
        var productId = await SeedProductAsync(images: 5);

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => UploadAsync(productId, Jpeg())));

        results.Count(r => r.Response.StatusCode == HttpStatusCode.Created).Should().Be(1);
        results.Count(r => (int)r.Response.StatusCode == 422).Should().Be(1);
        (await ImagesAsync(productId)).Should().HaveCount(6);
        Storage.Paths.Should().HaveCount(1, "only the winner wrote a blob");
    }

    [Fact]
    public async Task AC_A_33_TenConcurrentUploads_ToAnEmptyProduct_NeverExceedSix_AndLeaveOneConsistentPrimary()
    {
        var productId = await SeedProductAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => UploadAsync(productId, Jpeg())));

        results.Count(r => r.Response.StatusCode == HttpStatusCode.Created).Should().Be(6);
        results.Count(r => (int)r.Response.StatusCode == 422).Should().Be(4);
        var rows = await ImagesAsync(productId);
        rows.Should().HaveCount(6);
        rows.Count(r => r.IsPrimary).Should().Be(1);
        rows.Select(r => r.SortOrder).Should().OnlyHaveUniqueItems("appended order is serialised by the lock");
        Storage.Paths.Should().HaveCount(6);
    }

    [Fact]
    public async Task AC_A_33_TheLimitIsPerProduct()
    {
        var full = await SeedProductAsync("full", images: 6);
        var other = await SeedProductAsync("other");

        var (response, _, _) = await UploadAsync(other, Jpeg());

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await ImagesAsync(full)).Should().HaveCount(6);
    }

    [Fact]
    public void D_A2_TheLimitIsASingleNamedConstantOfSix() =>
        UTperfume.Application.Features.Admin.Images.ProductImageCommands.MaxImagesPerProduct.Should().Be(6);

    [Fact]
    public async Task BR_A_25_TheUploadLock_DoesNotChangeTheProductRowVersion()
    {
        var productId = await SeedProductAsync();
        byte[] before;
        await using (var db = _database.CreateContext())
        {
            before = (await db.Products.AsNoTracking().SingleAsync(p => p.Id == productId, Ct)).RowVersion;
        }

        (await UploadAsync(productId, Jpeg())).Response.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var after = _database.CreateContext();
        (await after.Products.AsNoTracking().SingleAsync(p => p.Id == productId, Ct)).RowVersion.Should().Equal(before,
            "an image upload must not make an open product form stale");
    }

    // ---- AC-A-36: compensation -----------------------------------------------------------------------------------

    [Fact]
    public async Task AC_A_36_WhenTheDatabaseStepFailsAfterTheUpload_TheBlobIsDeleted_AndNoRowRemains()
    {
        var productId = await SeedProductAsync();
        _failSave.FailNext = true;

        var (response, _, text) = await UploadAsync(productId, Jpeg());

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        text.Should().NotContain("simulated", "exception details are not leaked");
        Storage.Paths.Should().BeEmpty("the compensating delete removed the blob");
        (await TotalImageRowsAsync()).Should().Be(0);
        (await UploadAsync(productId, Jpeg())).Response.StatusCode.Should().Be(HttpStatusCode.Created, "the failure released the product lock");
    }

    [Fact]
    public async Task EC_A_StorageOutageDuringUpload_Returns503StorageUnavailable_StoresNothing_AndLeaksNoDetail()
    {
        var productId = await SeedProductAsync();
        Storage.FailUploads = true;

        var (response, json, text) = await UploadAsync(productId, Jpeg());

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        Code(json).Should().Be("storage_unavailable");
        text.Should().NotContainAny("SDK-DETAIL", "blob.core.windows.net", "secret-storage-text", "Simulated", "SQL", "Exception");
        Storage.Paths.Should().BeEmpty();
        (await TotalImageRowsAsync()).Should().Be(0);
        AuditEvents().Should().BeEmpty();

        Storage.FailUploads = false;
        (await UploadAsync(productId, Jpeg())).Response.StatusCode.Should().Be(HttpStatusCode.Created, "the failed upload released the product lock");
    }

    // ---- BR-A-31: stamping and audit ------------------------------------------------------------------------------

    [Fact]
    public async Task BR_A_31_AnUpload_StampsCreatedBy_AndCreatedAtUtc_AndWritesOneAuditEvent()
    {
        var productId = await SeedProductAsync();
        var before = DateTime.UtcNow.AddSeconds(-5);

        var (_, json, _) = await UploadAsync(productId, Jpeg());

        var row = (await ImagesAsync(productId)).Single();
        row.CreatedBy.Should().Be(AdminUser);
        row.CreatedAtUtc.Should().BeAfter(before).And.BeBefore(DateTime.UtcNow.AddSeconds(5));
        json.GetProperty("createdBy").GetString().Should().Be(AdminUser);
        json.GetProperty("createdAtUtc").GetDateTime().Should().BeCloseTo(row.CreatedAtUtc, TimeSpan.FromSeconds(1));
        var audit = AuditEvents().Should().ContainSingle().Subject;
        audit.State["Action"].Should().Be("image_upload");
        audit.State["EntityType"].Should().Be("ProductImage");
        audit.State["EntityId"].Should().Be(row.Id.ToString());
        audit.State["UserId"].Should().Be(AdminUser);
        audit.Message.Should().NotContain(_xsrf);
    }

    [Fact]
    public async Task BR_A_31_ARejectedUpload_WritesNoAuditEvent()
    {
        var productId = await SeedProductAsync(images: 6);

        await UploadAsync(productId, Jpeg());
        await UploadAsync(productId, Png(), "image/jpeg");

        AuditEvents().Should().BeEmpty();
    }

    // ---- public contract: BR-A-22, BR-A-27, BR-A-21 ----------------------------------------------------------------

    [Fact]
    public async Task BR_A_27_ThePublicProductDetail_ShowsTheUploadedImageByUrl_WithoutABlobPath_AfterTheCacheWasEvicted()
    {
        var productId = await SeedProductAsync("public-one", ProductStatus.Published, withVariant: true);
        using var beforeResponse = await _client.GetAsync("/api/v1/products/public-one", Ct);
        var beforeText = await beforeResponse.Content.ReadAsStringAsync(Ct);
        JsonDocument.Parse(beforeText).RootElement.GetProperty("images").GetArrayLength().Should().Be(0);

        var (_, json, _) = await UploadAsync(productId, Jpeg(), altText: "Front");

        using var afterResponse = await _client.GetAsync("/api/v1/products/public-one", Ct);
        var afterText = await afterResponse.Content.ReadAsStringAsync(Ct);
        var images = JsonDocument.Parse(afterText).RootElement.GetProperty("images");
        images.GetArrayLength().Should().Be(1, "the admin write evicted the cached public response (BR-A-21)");
        images[0].GetProperty("url").GetString().Should().Be(json.GetProperty("url").GetString());
        images[0].GetProperty("url").GetString().Should().StartWith(InMemoryImageStorage.PublicBaseUrl + "/products/");
        afterText.Should().NotContainEquivalentOf("blobPath");
    }

    [Fact]
    public async Task BR_A_26_TheStorageImplementation_WritesImmutableCacheControl()
    {
        // The in-memory fake cannot hold headers; the real property is written by AzureBlobImageStorage on every upload
        // (verified against Azurite in AzureBlobImageStorageAzuriteTests when Azurite runs).
        UTperfume.Infrastructure.Storage.AzureBlobImageStorage.ImmutableCacheControl.Should().Contain("immutable").And.Contain("max-age=31536000");
        await Task.CompletedTask;
    }

    // ---- DELETE -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AC_A_34_DeletingAnImage_Returns204_RemovesTheRowAndTheBlob_AndAuditsIt()
    {
        var productId = await SeedProductAsync();
        var (_, first, _) = await UploadAsync(productId, Jpeg());
        await UploadAsync(productId, Jpeg());
        var (_, third, _) = await UploadAsync(productId, Jpeg());
        var thirdId = third.GetProperty("id").GetInt32();
        var thirdPath = (await ImagesAsync(productId)).Single(i => i.Id == thirdId).BlobPath;

        var (response, _, _) = await SendAsync(DeleteRequest(productId, thirdId));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ImagesAsync(productId)).Select(i => i.Id).Should().NotContain(thirdId).And.HaveCount(2);
        Storage.Paths.Should().HaveCount(2).And.NotContain(thirdPath);
        var audit = AuditEvents().Last();
        audit.State["Action"].Should().Be("image_delete");
        audit.State["EntityType"].Should().Be("ProductImage");
        audit.State["EntityId"].Should().Be(thirdId.ToString());
        audit.State["UserId"].Should().Be(AdminUser);
        first.GetProperty("isPrimary").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task BR_A_28_DeletingThePrimary_PromotesTheRemainingImageWithTheLowestSortOrderThenId()
    {
        var productId = await SeedProductAsync();
        var ids = new List<int>();
        for (var i = 0; i < 3; i++)
        {
            ids.Add((await UploadAsync(productId, Jpeg())).Json.GetProperty("id").GetInt32());
        }

        var (response, _, _) = await SendAsync(DeleteRequest(productId, ids[0]));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var rows = await ImagesAsync(productId);
        rows.Where(r => r.IsPrimary).Should().ContainSingle().Which.Id.Should().Be(ids[1]);
    }

    [Fact]
    public async Task BR_A_28_DeletingANonPrimaryImage_LeavesThePrimaryUnchanged()
    {
        var productId = await SeedProductAsync();
        var ids = new List<int>();
        for (var i = 0; i < 3; i++)
        {
            ids.Add((await UploadAsync(productId, Jpeg())).Json.GetProperty("id").GetInt32());
        }

        await SendAsync(DeleteRequest(productId, ids[1]));

        (await ImagesAsync(productId)).Where(r => r.IsPrimary).Should().ContainSingle().Which.Id.Should().Be(ids[0]);
    }

    [Fact]
    public async Task BR_A_10_DeletingTheSameImageTwice_Returns204ThenA404_AndChangesNothingTheSecondTime()
    {
        var productId = await SeedProductAsync();
        await UploadAsync(productId, Jpeg());
        var (_, second, _) = await UploadAsync(productId, Jpeg());
        var id = second.GetProperty("id").GetInt32();

        var (first, _, _) = await SendAsync(DeleteRequest(productId, id));
        var pathsAfterFirst = Storage.Paths.ToList();
        var auditsAfterFirst = AuditEvents().Count;
        var (again, _, _) = await SendAsync(DeleteRequest(productId, id));

        first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        again.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Storage.Paths.Should().BeEquivalentTo(pathsAfterFirst);
        (await ImagesAsync(productId)).Should().HaveCount(1);
        AuditEvents().Count.Should().Be(auditsAfterFirst, "a delete that found nothing writes no audit event");
    }

    [Fact]
    public async Task BR_A_10_AnImageOfAnotherProduct_OrAnUnknownProduct_Returns404_AndIsNotDeleted()
    {
        var one = await SeedProductAsync("one");
        var two = await SeedProductAsync("two");
        var (_, image, _) = await UploadAsync(one, Jpeg());
        var id = image.GetProperty("id").GetInt32();

        var (wrongProduct, _, _) = await SendAsync(DeleteRequest(two, id));
        var (unknownProduct, _, _) = await SendAsync(DeleteRequest(987654, id));

        wrongProduct.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknownProduct.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ImagesAsync(one)).Should().HaveCount(1);
        Storage.Paths.Should().HaveCount(1);
    }

    [Fact]
    public async Task BR_A_15_TheLastImageOfAPublishedProduct_Cannot_BeDeleted_422()
    {
        var productId = await SeedProductAsync("pub", ProductStatus.Published, images: 1, withVariant: true);
        var id = (await ImagesAsync(productId)).Single().Id;

        var (response, json, _) = await SendAsync(DeleteRequest(productId, id));

        response.StatusCode.Should().Be((HttpStatusCode)422);
        Code(json).Should().Be("published_product_requirement");
        (await ImagesAsync(productId)).Should().HaveCount(1);
    }

    [Fact]
    public async Task BR_A_15_ANonLastImageOfAPublishedProduct_CanBeDeleted_AndTheLastImageOfADraftCan()
    {
        var published = await SeedProductAsync("pub", ProductStatus.Published, images: 2, withVariant: true);
        var draft = await SeedProductAsync("draft", ProductStatus.Draft, images: 1);

        var (fromPublished, _, _) = await SendAsync(DeleteRequest(published, (await ImagesAsync(published))[1].Id));
        var (fromDraft, _, _) = await SendAsync(DeleteRequest(draft, (await ImagesAsync(draft)).Single().Id));

        fromPublished.StatusCode.Should().Be(HttpStatusCode.NoContent);
        fromDraft.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ImagesAsync(draft)).Should().BeEmpty();
    }

    [Fact]
    public async Task BR_A_26_AFailingBlobDelete_IsLogged_AndDoesNotFailTheRequest_TheRowIsGone()
    {
        var productId = await SeedProductAsync();
        await UploadAsync(productId, Jpeg());
        var (_, second, _) = await UploadAsync(productId, Jpeg());
        Storage.FailDeletes = true;
        try
        {
            var (response, _, _) = await SendAsync(DeleteRequest(productId, second.GetProperty("id").GetInt32()));

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }
        finally
        {
            Storage.FailDeletes = false;
        }

        (await ImagesAsync(productId)).Should().HaveCount(1);
        _factory.Logs.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("Deleting the blob failed"));
    }

    [Fact]
    public async Task BR_A_21_DeletingAnImage_EvictsThePublicCache()
    {
        var productId = await SeedProductAsync("pub-del", ProductStatus.Published, withVariant: true);
        await UploadAsync(productId, Jpeg());
        var (_, second, _) = await UploadAsync(productId, Jpeg());
        using var cached = await _client.GetAsync("/api/v1/products/pub-del", Ct);
        JsonDocument.Parse(await cached.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("images").GetArrayLength().Should().Be(2);

        await SendAsync(DeleteRequest(productId, second.GetProperty("id").GetInt32()));

        using var after = await _client.GetAsync("/api/v1/products/pub-del", Ct);
        JsonDocument.Parse(await after.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("images").GetArrayLength().Should().Be(1);
    }

    // ---- authorization gates ------------------------------------------------------------------------------------------

    [Fact]
    public async Task AC_A1_Anonymous_Upload_And_Delete_Return401_AndChangeNothing()
    {
        var productId = await SeedProductAsync(images: 1);
        var imageId = (await ImagesAsync(productId)).Single().Id;

        var (upload, _, _) = await SendAsync(UploadRequest(productId, Jpeg(), authenticate: false));
        var (delete, _, _) = await SendAsync(DeleteRequest(productId, imageId, authenticate: false));

        upload.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        delete.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ImagesAsync(productId)).Should().HaveCount(1);
        Storage.Paths.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("Customer")]
    public async Task AC_A2_AnAuthenticatedUserWithoutTheAdminRole_Gets403_OnUploadAndDelete(string roles)
    {
        var productId = await SeedProductAsync(images: 1);
        var imageId = (await ImagesAsync(productId)).Single().Id;

        var (upload, _, _) = await SendAsync(UploadRequest(productId, Jpeg(), roles: roles));
        var (delete, _, _) = await SendAsync(DeleteRequest(productId, imageId, roles: roles));

        upload.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        delete.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ImagesAsync(productId)).Should().HaveCount(1);
        Storage.Paths.Should().BeEmpty();
    }

    // ---- antiforgery: BR-A-04, AC-A-05, AC-A-38 -----------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("forged-token")]
    public async Task AC_A_05_UploadWithAMissingOrForgedToken_Returns400AntiforgeryInvalid_AndStoresNothing(string? token)
    {
        var productId = await SeedProductAsync();

        var (response, json, _) = await SendAsync(UploadRequest(productId, Jpeg(), xsrf: token));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Code(json).Should().Be("antiforgery_invalid");
        Storage.Paths.Should().BeEmpty();
        (await TotalImageRowsAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("forged-token")]
    public async Task AC_A_05_DeleteWithAMissingOrForgedToken_Returns400AntiforgeryInvalid_AndDeletesNothing(string? token)
    {
        var productId = await SeedProductAsync(images: 2);
        var imageId = (await ImagesAsync(productId))[1].Id;

        var (response, json, _) = await SendAsync(DeleteRequest(productId, imageId, xsrf: token));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Code(json).Should().Be("antiforgery_invalid");
        (await ImagesAsync(productId)).Should().HaveCount(2);
    }

    [Fact]
    public async Task AC_A_38_UploadWithAValidToken_ReadsTheMultipartForm_AndSucceeds_WithoutADisableAntiforgeryOnTheRoute()
    {
        var productId = await SeedProductAsync();

        var (response, _, _) = await SendAsync(UploadRequest(productId, Jpeg()));

        response.StatusCode.Should().Be(HttpStatusCode.Created, "the admin group makes the framework's IFormFile antiforgery check harmless (F1)");
    }

    // ---- test doubles ---------------------------------------------------------------------------------------------------

    private sealed class FailureSwitch
    {
        public bool FailNext { get; set; }
    }

    /// <summary>Makes the next save that inserts a product image fail, as a database outage would.</summary>
    private sealed class FailableSession(IAdminWriteSession inner, FailureSwitch failure) : IAdminWriteSession
    {
        public void ExpectRowVersion(AuditableEntity entity, byte[] rowVersion) => inner.ExpectRowVersion(entity, rowVersion);

        public Task<Error?> SaveChangesAsync(CancellationToken cancellationToken)
        {
            if (failure.FailNext)
            {
                failure.FailNext = false;
                throw new InvalidOperationException("simulated database failure");
            }

            return inner.SaveChangesAsync(cancellationToken);
        }

        public Task<Error?> SaveServerSideChangesAsync(CancellationToken cancellationToken) => inner.SaveServerSideChangesAsync(cancellationToken);
    }
}
