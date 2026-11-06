using System.Net;
using System.Text.Json;
using UTperfume.Application.Domain;
using UTperfume.IntegrationTests.Infrastructure;

namespace UTperfume.IntegrationTests.Api;

public sealed class HomeEndpointsTests(TestDatabase database) : CatalogApiTestBase(database)
{
    private static readonly string[] Sections =
        ["banners", "featuredProducts", "newArrivals", "bestSellers", "popularBrands", "categories"];

    private static string[] Slugs(JsonElement section) =>
        section.EnumerateArray().Select(e => e.GetProperty("slug").GetString()!).ToArray();

    private static Product Featured(Brand brand, string slug, DateTime published, bool featured = true, bool best = false)
    {
        var product = NewProduct(brand, slug, publishedAt: published, variants: NewVariant(slug.ToUpperInvariant(), 50, 1_000_000));
        product.IsFeatured = featured;
        product.IsBestSeller = best;
        return product;
    }

    // AC-H1
    [Fact]
    public async Task Featured_products_are_capped_at_8_and_ordered_by_published_date_descending()
    {
        var brand = NewBrand("b");
        var products = Enumerable.Range(1, 10)
            .Select(i => Featured(brand, $"feat-{i:00}", Now.AddDays(-i)))
            .Append(Featured(brand, "not-featured", Now.AddDays(1), featured: false))
            .ToArray<object>();
        await SaveAsync(products);

        var (response, json) = await GetJsonAsync("/api/v1/home");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Slugs(json.GetProperty("featuredProducts")).Should().Equal(
            Enumerable.Range(1, 8).Select(i => $"feat-{i:00}"));
    }

    // AC-H2
    [Fact]
    public async Task A_product_of_an_inactive_brand_appears_in_no_section()
    {
        var inactive = NewBrand("inactive", active: false, featured: true);
        var hidden = Featured(inactive, "hidden", Now, best: true);
        var category = NewCategory("only-hidden");
        hidden.Categories.Add(new ProductCategory { Category = category });
        await SaveAsync(hidden, NewBrand("active"));

        var (_, json) = await GetJsonAsync("/api/v1/home");

        foreach (var section in new[] { "featuredProducts", "newArrivals", "bestSellers" })
        {
            Slugs(json.GetProperty(section)).Should().NotContain("hidden", section);
        }

        Slugs(json.GetProperty("popularBrands")).Should().NotContain("inactive");
        Slugs(json.GetProperty("categories")).Should().NotContain("only-hidden", "a category with only hidden products is not offered");
    }

    [Fact]
    public async Task Draft_archived_and_no_active_variant_products_appear_in_no_section()
    {
        var brand = NewBrand("b");
        var draft = Featured(brand, "draft", Now, best: true);
        draft.Status = ProductStatus.Draft;
        draft.PublishedAtUtc = null;
        var archived = Featured(brand, "archived", Now, best: true);
        archived.Status = ProductStatus.Archived;
        var noVariant = NewProduct(brand, "no-variant", variants: NewVariant("NV", 50, 100, active: false));
        noVariant.IsFeatured = true;
        noVariant.IsBestSeller = true;
        await SaveAsync(draft, archived, noVariant, Featured(brand, "ok", Now.AddDays(-1), best: true));

        var (_, json) = await GetJsonAsync("/api/v1/home");

        foreach (var section in new[] { "featuredProducts", "newArrivals", "bestSellers" })
        {
            Slugs(json.GetProperty(section)).Should().Equal("ok");
        }
    }

    // AC-H3
    [Fact]
    public async Task Banners_outside_their_time_window_or_inactive_are_not_returned()
    {
        var now = DateTime.UtcNow;
        HomeBanner Banner(string title, int sort, DateTime? start, DateTime? end, bool active = true, string? link = "/perfumes") =>
            new()
            {
                Title = title, ImageBlobPath = $"banners/{title}.png", AltText = title, LinkUrl = link, SortOrder = sort,
                IsActive = active, StartsAtUtc = start, EndsAtUtc = end, CreatedAtUtc = Now, UpdatedAtUtc = Now,
            };

        await SaveAsync(
            Banner("always", 3, null, null),
            Banner("current", 1, now.AddDays(-1), now.AddDays(1)),
            Banner("open-ended", 2, now.AddDays(-1), null),
            Banner("expired", 0, now.AddDays(-10), now.AddDays(-1)),
            Banner("future", 0, now.AddDays(1), now.AddDays(10)),
            Banner("inactive", 0, null, null, active: false));

        var (_, json) = await GetJsonAsync("/api/v1/home");

        var banners = json.GetProperty("banners").EnumerateArray().ToList();
        banners.Select(b => b.GetProperty("title").GetString()).Should().Equal("current", "open-ended", "always");
        banners[0].GetProperty("imageUrl").GetString().Should().Be("https://images.test/catalog-images/banners/current.png");
        banners[0].GetProperty("linkUrl").GetString().Should().Be("/perfumes");
        banners[0].GetProperty("altText").GetString().Should().Be("current");
    }

    [Fact]
    public async Task At_most_five_banners_are_returned_in_sort_order()
    {
        var banners = Enumerable.Range(1, 7).Select(i => new HomeBanner
        {
            Title = $"b{i}", ImageBlobPath = $"banners/{i}.png", AltText = "a", SortOrder = 8 - i, CreatedAtUtc = Now, UpdatedAtUtc = Now,
        }).ToArray<object>();
        await SaveAsync(banners);

        var (_, json) = await GetJsonAsync("/api/v1/home");

        json.GetProperty("banners").EnumerateArray().Select(b => b.GetProperty("title").GetString())
            .Should().Equal("b7", "b6", "b5", "b4", "b3");
    }

    // AC-H4
    [Fact]
    public async Task With_no_data_every_section_is_an_empty_array()
    {
        var (response, json) = await GetJsonAsync("/api/v1/home");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        foreach (var section in Sections)
        {
            json.GetProperty(section).ValueKind.Should().Be(JsonValueKind.Array, section);
            json.GetProperty(section).GetArrayLength().Should().Be(0, section);
        }
    }

    // AC-H5
    [Fact]
    public async Task A_featured_brand_without_visible_products_is_not_a_popular_brand()
    {
        var withProducts = NewBrand("with", featured: true);
        var withoutVisible = NewBrand("without", featured: true);
        var notFeatured = NewBrand("plain", featured: false);
        await SaveAsync(
            Featured(withProducts, "w1", Now, featured: false),
            Featured(notFeatured, "n1", Now, featured: false),
            NewProduct(withoutVisible, "draft-only", status: ProductStatus.Draft, variants: NewVariant("DO", 50, 100)));

        var (_, json) = await GetJsonAsync("/api/v1/home");

        Slugs(json.GetProperty("popularBrands")).Should().Equal("with");
        json.GetProperty("popularBrands")[0].GetProperty("productCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Popular_brands_are_ordered_by_display_order_then_name_and_capped_at_12()
    {
        var brands = Enumerable.Range(1, 14).Select(i =>
        {
            var b = NewBrand($"brand-{i:00}", featured: true);
            b.DisplayOrder = 15 - i;
            b.Name = $"Brand {i:00}";
            return b;
        }).ToList();
        var products = brands.Select((b, i) => (object)Featured(b, $"p-{i:00}", Now, featured: false)).ToArray();
        await SaveAsync(products);

        var (_, json) = await GetJsonAsync("/api/v1/home");

        Slugs(json.GetProperty("popularBrands")).Should().Equal(
            Enumerable.Range(1, 14).Reverse().Take(12).Select(i => $"brand-{i:00}"));
    }

    [Fact]
    public async Task Categories_are_active_with_visible_products_ordered_and_capped_at_12()
    {
        var brand = NewBrand("b");
        var cats = Enumerable.Range(1, 14).Select(i => NewCategory($"cat-{i:00}", order: 15 - i)).ToList();
        var empty = NewCategory("empty");
        var retired = NewCategory("retired", active: false);
        var products = cats.Select((c, i) =>
        {
            var p = Featured(brand, $"p-{i:00}", Now, featured: false);
            p.Categories.Add(new ProductCategory { Category = c });
            return p;
        }).ToList<object>();
        var retiredProduct = Featured(brand, "r", Now, featured: false);
        retiredProduct.Categories.Add(new ProductCategory { Category = retired });
        products.Add(retiredProduct);
        products.Add(empty);
        await SaveAsync([.. products]);

        var (_, json) = await GetJsonAsync("/api/v1/home");

        Slugs(json.GetProperty("categories")).Should().Equal(
            Enumerable.Range(1, 14).Reverse().Take(12).Select(i => $"cat-{i:00}"));
    }

    [Fact]
    public async Task New_arrivals_and_best_sellers_follow_their_rules_and_are_capped_at_8()
    {
        var brand = NewBrand("b");
        var products = Enumerable.Range(1, 10)
            .Select(i => Featured(brand, $"p-{i:00}", Now.AddDays(-i), featured: false, best: i % 2 == 0))
            .ToArray<object>();
        await SaveAsync(products);

        var (_, json) = await GetJsonAsync("/api/v1/home");

        Slugs(json.GetProperty("newArrivals")).Should().Equal(Enumerable.Range(1, 8).Select(i => $"p-{i:00}"));
        Slugs(json.GetProperty("bestSellers")).Should().Equal("p-02", "p-04", "p-06", "p-08", "p-10");
        json.GetProperty("featuredProducts").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Home_items_use_the_summary_shape_and_expose_no_quantity_or_blob_path()
    {
        var brand = NewBrand("b", logo: "brands/b.png");
        brand.IsFeatured = true;
        var product = Featured(brand, "p", Now);
        product.Images.Add(new ProductImage { BlobPath = "products/1/x.png", ContentType = "image/png", SizeBytes = 1, IsPrimary = true, CreatedAtUtc = Now });
        await SaveAsync(product, new HomeBanner { Title = "t", ImageBlobPath = "banners/t.png", AltText = "a", CreatedAtUtc = Now, UpdatedAtUtc = Now });

        var body = await Client.GetStringAsync("/api/v1/home", Ct);

        body.Should().NotContainEquivalentOf("quantity").And.NotContainEquivalentOf("blobPath");
        var json = JsonDocument.Parse(body).RootElement;
        var item = json.GetProperty("featuredProducts")[0];
        item.GetProperty("fromPrice").GetRawText().Should().Be("1000000");
        item.GetProperty("currency").GetString().Should().Be("VND");
        item.GetProperty("primaryImage").GetProperty("url").GetString().Should().Be("https://images.test/catalog-images/products/1/x.png");
        json.GetProperty("popularBrands")[0].GetProperty("logoUrl").GetString().Should().Be("https://images.test/catalog-images/brands/b.png");
    }
}
