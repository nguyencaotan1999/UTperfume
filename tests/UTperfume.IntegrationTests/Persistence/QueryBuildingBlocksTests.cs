using Microsoft.EntityFrameworkCore;
using UTperfume.Application.Common;
using UTperfume.Application.Domain;
using UTperfume.Application.Features.Products;
using UTperfume.IntegrationTests.Infrastructure;

namespace UTperfume.IntegrationTests.Persistence;

/// <summary>P1-04: visibility (BR-01), from-price (BR-11) and SQL-side availability (BR-08), on real SQL Server.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class QueryBuildingBlocksTests(TestDatabase database) : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public async ValueTask InitializeAsync() => await database.ResetCatalogAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static Brand Brand(string slug, bool active = true) =>
        new() { Name = slug, Slug = slug, IsActive = active, CreatedAtUtc = Now, UpdatedAtUtc = Now };

    private static Product Product(Brand brand, string slug, ProductStatus status = ProductStatus.Published,
        params ProductVariant[] variants)
    {
        var product = new Product
        {
            Brand = brand, ConcentrationId = 3, Name = slug, Slug = slug, Status = status,
            PublishedAtUtc = status == ProductStatus.Draft ? null : Now, CreatedAtUtc = Now, UpdatedAtUtc = Now,
        };
        product.Variants.AddRange(variants);
        return product;
    }

    private static ProductVariant Variant(string sku, decimal volume, long price, long? original = null, int stock = 10,
        bool active = true) =>
        new()
        {
            Sku = sku, VolumeMl = volume, Price = price, OriginalPrice = original, StockQuantity = stock,
            IsActive = active, CreatedAtUtc = Now, UpdatedAtUtc = Now,
        };

    private async Task SaveAsync(params Product[] products)
    {
        await using var context = database.CreateContext();
        context.Products.AddRange(products);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task VisibleProducts_requires_published_active_brand_and_an_active_variant()
    {
        var active = Brand("active");
        var inactive = Brand("inactive", active: false);
        await SaveAsync(
            Product(active, "visible", ProductStatus.Published, Variant("V-1", 50, 100)),
            Product(active, "visible-mixed", ProductStatus.Published, Variant("V-2", 50, 100, active: false), Variant("V-3", 100, 200)),
            Product(active, "draft", ProductStatus.Draft, Variant("D-1", 50, 100)),
            Product(active, "archived", ProductStatus.Archived, Variant("A-1", 50, 100)),
            Product(inactive, "inactive-brand", ProductStatus.Published, Variant("I-1", 50, 100)),
            Product(active, "no-variants", ProductStatus.Published),
            Product(active, "only-inactive-variants", ProductStatus.Published, Variant("X-1", 50, 100, active: false)));

        await using var context = database.CreateContext();
        var slugs = await context.Products.VisibleProducts().Select(p => p.Slug).OrderBy(s => s)
            .ToListAsync(TestContext.Current.CancellationToken);

        slugs.Should().Equal("visible", "visible-mixed");
    }

    [Fact]
    public async Task Product_in_only_an_inactive_category_stays_visible()
    {
        var product = Product(Brand("b"), "in-inactive-category", ProductStatus.Published, Variant("C-1", 50, 100));
        product.Categories.Add(new ProductCategory
        {
            Category = new Category { Name = "Old", Slug = "old", IsActive = false, CreatedAtUtc = Now, UpdatedAtUtc = Now },
        });
        await SaveAsync(product);

        await using var context = database.CreateContext();
        (await context.Products.VisibleProducts().CountAsync(TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task FromPrice_is_the_minimum_over_active_variants_only()
    {
        await SaveAsync(Product(Brand("b"), "p", ProductStatus.Published,
            Variant("P-30", 30, 900_000, active: false),
            Variant("P-50", 50, 1_250_000),
            Variant("P-100", 100, 2_000_000)));

        await using var context = database.CreateContext();
        var row = await context.Products.VisibleProducts().ToSummaryRows().SingleAsync(TestContext.Current.CancellationToken);

        row.FromPrice.Should().Be(1_250_000);
        row.VolumesMl.Should().Equal(50m, 100m);
    }

    [Fact]
    public async Task FromPrice_with_a_volume_filter_uses_only_the_selected_volumes()
    {
        await SaveAsync(Product(Brand("b"), "p", ProductStatus.Published,
            Variant("P-50", 50, 1_250_000),
            Variant("P-100", 100, 2_000_000),
            Variant("P-200", 200, 3_000_000)));

        await using var context = database.CreateContext();
        var ct = TestContext.Current.CancellationToken;

        var unfiltered = await context.Products.VisibleProducts().ToSummaryRows().SingleAsync(ct);
        var onlyBig = await context.Products.VisibleProducts().ToSummaryRows([100m, 200m]).SingleAsync(ct);
        var decimalVolume = await context.Products.VisibleProducts().ToSummaryRows([200.0m]).SingleAsync(ct);

        unfiltered.FromPrice.Should().Be(1_250_000);
        onlyBig.FromPrice.Should().Be(2_000_000);
        decimalVolume.FromPrice.Should().Be(3_000_000);
        onlyBig.VolumesMl.Should().Equal([50m, 100m, 200m], "the volume list always shows every active volume");
    }


    [Fact]
    public async Task Volume_filter_excludes_products_without_an_active_variant_in_those_volumes_and_never_yields_zero_price()
    {
        await SaveAsync(
            Product(Brand("b"), "has-100", ProductStatus.Published, Variant("H-50", 50, 1_000_000), Variant("H-100", 100, 2_000_000)),
            Product(Brand("c"), "only-50", ProductStatus.Published, Variant("O-50", 50, 500_000)),
            Product(Brand("d"), "inactive-100", ProductStatus.Published,
                Variant("I-50", 50, 700_000), Variant("I-100", 100, 100_000, active: false)));

        await using var context = database.CreateContext();
        var rows = await context.Products.VisibleProducts().ToSummaryRows([100m]).ToListAsync(TestContext.Current.CancellationToken);

        rows.Select(r => r.Slug).Should().Equal("has-100");
        rows.Should().OnlyContain(r => r.FromPrice > 0);

        // A max-price style filter on the from-price can therefore never match a product with no such volume.
        (await context.Products.VisibleProducts().ToSummaryRows([30m]).CountAsync(TestContext.Current.CancellationToken))
            .Should().Be(0);
    }
    [Fact]
    public async Task Compare_at_price_comes_from_the_cheapest_variant_and_ties_use_the_lowest_volume()
    {
        await SaveAsync(
            Product(Brand("b"), "tie", ProductStatus.Published,
                Variant("T-100", 100, 1_000_000, original: 1_500_000),
                Variant("T-50", 50, 1_000_000, original: 1_200_000),
                Variant("T-200", 200, 2_000_000, original: 9_000_000)),
            Product(Brand("c"), "no-compare", ProductStatus.Published, Variant("N-1", 50, 500_000)));

        await using var context = database.CreateContext();
        var rows = await context.Products.VisibleProducts().ToSummaryRows()
            .ToDictionaryAsync(r => r.Slug, TestContext.Current.CancellationToken);

        rows["tie"].FromPrice.Should().Be(1_000_000);
        rows["tie"].FromOriginalPrice.Should().Be(1_200_000);
        rows["no-compare"].FromOriginalPrice.Should().BeNull();
    }

    [Fact]
    public async Task InStock_is_true_when_any_active_variant_has_stock_and_ignores_inactive_ones()
    {
        await SaveAsync(
            Product(Brand("b"), "has-stock", ProductStatus.Published, Variant("S-1", 50, 100, stock: 0), Variant("S-2", 100, 200, stock: 1)),
            Product(Brand("c"), "sold-out", ProductStatus.Published, Variant("O-1", 50, 100, stock: 0)),
            Product(Brand("d"), "only-inactive-has-stock", ProductStatus.Published,
                Variant("H-1", 50, 100, stock: 0), Variant("H-2", 100, 200, stock: 50, active: false)));

        await using var context = database.CreateContext();
        var rows = await context.Products.ToSummaryRows().ToDictionaryAsync(r => r.Slug, TestContext.Current.CancellationToken);

        rows["has-stock"].InStock.Should().BeTrue();
        rows["sold-out"].InStock.Should().BeFalse();
        rows["only-inactive-has-stock"].InStock.Should().BeFalse();
    }

    [Fact]
    public async Task Primary_image_is_preferred_then_sort_order()
    {
        var product = Product(Brand("b"), "imgs", ProductStatus.Published, Variant("I-1", 50, 100));
        product.Images.AddRange(
            new ProductImage { BlobPath = "products/1/a.png", ContentType = "image/png", SizeBytes = 1, SortOrder = 0, CreatedAtUtc = Now },
            new ProductImage { BlobPath = "products/1/b.png", ContentType = "image/png", SizeBytes = 1, SortOrder = 5, IsPrimary = true, CreatedAtUtc = Now });
        var none = Product(Brand("c"), "no-image", ProductStatus.Published, Variant("I-2", 50, 100));
        await SaveAsync(product, none);

        await using var context = database.CreateContext();
        var rows = await context.Products.ToSummaryRows().ToDictionaryAsync(r => r.Slug, TestContext.Current.CancellationToken);

        rows["imgs"].PrimaryImageBlobPath.Should().Be("products/1/b.png");
        rows["no-image"].PrimaryImageBlobPath.Should().BeNull();
    }

    [Theory]
    [InlineData(5)]
    [InlineData(1)]
    [InlineData(10)]
    public async Task Availability_case_runs_in_sql_and_matches_the_domain_rule_at_every_boundary(int threshold)
    {
        var quantities = new[] { 0, 1, threshold, threshold + 1, threshold + 100 }.Distinct().ToArray();
        var variants = quantities.Select((q, i) => Variant($"AV-{i}", 10 + i, 100, stock: q)).ToArray();
        await SaveAsync(Product(Brand("b"), "av", ProductStatus.Published, variants));

        await using var context = database.CreateContext();
        var ct = TestContext.Current.CancellationToken;
        var rows = await context.ProductVariants.ToAvailabilityRows(threshold).ToListAsync(ct);

        rows.Should().HaveCount(quantities.Length);
        var byVolume = rows.OrderBy(r => r.VolumeMl).ToList();
        for (var i = 0; i < quantities.Length; i++)
        {
            byVolume[i].Availability.Should().Be(Availability.From(quantities[i], threshold),
                $"quantity {quantities[i]} with threshold {threshold}");
        }

        context.ProductVariants.ToAvailabilityRows(threshold).ToQueryString()
            .Should().Contain("CASE").And.Contain("WHEN").And.NotContain("StockQuantity] AS");
    }

    [Fact]
    public async Task Availability_rows_skip_inactive_variants()
    {
        await SaveAsync(Product(Brand("b"), "av", ProductStatus.Published,
            Variant("A-1", 50, 100, stock: 3), Variant("A-2", 100, 200, stock: 3, active: false)));

        await using var context = database.CreateContext();
        var rows = await context.ProductVariants.ToAvailabilityRows(5).ToListAsync(TestContext.Current.CancellationToken);

        rows.Should().ContainSingle().Which.Sku.Should().Be("A-1");
    }

    [Fact]
    public async Task Summary_sql_never_selects_the_stock_quantity_column_into_the_result()
    {
        await using var context = database.CreateContext();

        var sql = context.Products.VisibleProducts().ToSummaryRows().ToQueryString();

        // StockQuantity may appear in a predicate (any variant with stock) but must not be a selected column.
        sql.Should().NotContain("[v].[StockQuantity] AS");
        sql.Should().NotMatchRegex(@"SELECT[^;]*\[v\d*\]\.\[StockQuantity\]\s*,");
    }
}
