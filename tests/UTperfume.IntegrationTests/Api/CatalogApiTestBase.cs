using System.Net.Http.Json;
using System.Text.Json;
using UTperfume.Application.Domain;
using UTperfume.IntegrationTests.Infrastructure;

namespace UTperfume.IntegrationTests.Api;

/// <summary>
/// Shared setup for public catalogue endpoint tests: an in-process API on the per-run SQL Server database, an empty
/// catalogue before each test, and small builders. Concentration ids: 1 PARFUM, 2 EXTRAIT, 3 EDP, 4 EDT.
/// </summary>
[Collection(DatabaseCollection.Name)]
public abstract class CatalogApiTestBase(TestDatabase database) : IAsyncLifetime, IDisposable
{
    protected static readonly DateTime Now = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly ApiFactory _factory = new(database);

    protected TestDatabase Database => database;

    protected ApiFactory Factory => _factory;

    protected HttpClient Client { get; private set; } = null!;

    protected CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await database.ResetCatalogAsync();
        Client = _factory.CreateClient();
    }

    public ValueTask DisposeAsync()
    {
        Client?.Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose() => _factory.Dispose();

    protected static Brand NewBrand(string slug, bool active = true, string? logo = null, bool featured = false) =>
        new()
        {
            Name = slug, Slug = slug, IsActive = active, LogoBlobPath = logo, IsFeatured = featured,
            CreatedAtUtc = Now, UpdatedAtUtc = Now,
        };

    protected static Category NewCategory(string slug, bool active = true, int order = 0, string? image = null) =>
        new() { Name = slug, Slug = slug, IsActive = active, DisplayOrder = order, ImageBlobPath = image, CreatedAtUtc = Now, UpdatedAtUtc = Now };

    protected static Product NewProduct(
        Brand brand, string slug, short concentrationId = 3, ProductStatus status = ProductStatus.Published,
        DateTime? publishedAt = null, string? name = null, params ProductVariant[] variants)
    {
        var product = new Product
        {
            Brand = brand, ConcentrationId = concentrationId, Name = name ?? slug, Slug = slug, Status = status,
            PublishedAtUtc = status == ProductStatus.Draft ? null : publishedAt ?? Now,
            CreatedAtUtc = Now, UpdatedAtUtc = Now,
        };
        product.Variants.AddRange(variants);
        return product;
    }

    protected static ProductVariant NewVariant(
        string sku, decimal volume, long price, long? original = null, int stock = 10, bool active = true) =>
        new()
        {
            Sku = sku, VolumeMl = volume, Price = price, OriginalPrice = original, StockQuantity = stock,
            IsActive = active, CreatedAtUtc = Now, UpdatedAtUtc = Now,
        };

    protected async Task SaveAsync(params object[] entities)
    {
        await using var context = database.CreateContext();
        context.AddRange(entities);
        await context.SaveChangesAsync(Ct);
    }

    protected async Task<(HttpResponseMessage Response, JsonElement Json)> GetJsonAsync(string url)
    {
        var response = await Client.GetAsync(url, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        using var document = JsonDocument.Parse(body);
        return (response, document.RootElement.Clone());
    }

    protected static void AssertProblem(HttpResponseMessage response, JsonElement json, int status)
    {
        ((int)response.StatusCode).Should().Be(status);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        json.GetProperty("status").GetInt32().Should().Be(status);
        json.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
    }
}
