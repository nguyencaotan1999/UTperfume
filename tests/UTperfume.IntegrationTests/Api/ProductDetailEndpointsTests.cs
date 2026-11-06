using System.Net;
using System.Text.Json;
using UTperfume.Application.Domain;
using UTperfume.IntegrationTests.Infrastructure;

namespace UTperfume.IntegrationTests.Api;

public sealed class ProductDetailEndpointsTests(TestDatabase database) : CatalogApiTestBase(database)
{
    private static string[] Slugs(JsonElement json) =>
        json.EnumerateArray().Select(i => i.GetProperty("slug").GetString()!).ToArray();

    private static ProductImage Image(string path, int sort, bool primary = false, string? alt = null) =>
        new()
        {
            BlobPath = path, ContentType = "image/png", SizeBytes = 10, SortOrder = sort, IsPrimary = primary,
            AltText = alt, Width = 800, Height = 600, CreatedAtUtc = Now,
        };

    private static FragranceNote Note(string slug) => new() { Name = slug, Slug = slug, CreatedAtUtc = Now };

    // AC-P1
    [Fact]
    public async Task Detail_returns_brand_concentration_categories_images_notes_and_variants()
    {
        var brand = NewBrand("dior");
        var men = NewCategory("men", order: 0);
        var winter = NewCategory("winter", order: 1);
        var retired = NewCategory("retired", active: false);
        var product = NewProduct(brand, "sauvage", name: "Sauvage", variants:
        [
            NewVariant("SAU-100", 100, 3_200_000, original: 4_000_000),
            NewVariant("SAU-50", 50, 2_000_000),
            NewVariant("SAU-30", 30, 1_000_000, active: false),
        ]);
        product.ShortDescription = "Fresh and woody";
        product.Description = "Long description";
        product.Categories.AddRange([new ProductCategory { Category = winter }, new ProductCategory { Category = men }, new ProductCategory { Category = retired }]);
        product.Images.AddRange([Image("products/1/c.png", 2), Image("products/1/a.png", 5, primary: true, alt: "Front"), Image("products/1/b.png", 1)]);
        product.Notes.AddRange(
        [
            new ProductNote { NoteType = NoteType.Base, SortOrder = 0, FragranceNote = Note("amber") },
            new ProductNote { NoteType = NoteType.Top, SortOrder = 1, FragranceNote = Note("lemon") },
            new ProductNote { NoteType = NoteType.Top, SortOrder = 0, FragranceNote = Note("bergamot") },
            new ProductNote { NoteType = NoteType.Heart, SortOrder = 0, FragranceNote = Note("pepper") },
        ]);
        await SaveAsync(product);

        var (response, json) = await GetJsonAsync("/api/v1/products/sauvage");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.GetProperty("slug").GetString().Should().Be("sauvage");
        json.GetProperty("brand").GetProperty("slug").GetString().Should().Be("dior");
        json.GetProperty("concentration").GetProperty("code").GetString().Should().Be("EDP");
        json.GetProperty("shortDescription").GetString().Should().Be("Fresh and woody");
        json.GetProperty("currency").GetString().Should().Be("VND");
        json.GetProperty("categories").EnumerateArray().Select(c => c.GetProperty("slug").GetString())
            .Should().Equal("men", "winter");

        var images = json.GetProperty("images").EnumerateArray().ToList();
        images.Select(i => i.GetProperty("url").GetString()).Should().Equal(
            "https://images.test/catalog-images/products/1/a.png",
            "https://images.test/catalog-images/products/1/b.png",
            "https://images.test/catalog-images/products/1/c.png");
        images[0].GetProperty("isPrimary").GetBoolean().Should().BeTrue();
        images[0].GetProperty("altText").GetString().Should().Be("Front");
        images[1].GetProperty("altText").GetString().Should().Be("Sauvage", "alt text falls back to the product name");
        images[0].GetProperty("width").GetInt32().Should().Be(800);

        var notes = json.GetProperty("notes");
        notes.GetProperty("top").EnumerateArray().Select(n => n.GetProperty("slug").GetString()).Should().Equal("bergamot", "lemon");
        notes.GetProperty("heart").EnumerateArray().Select(n => n.GetProperty("slug").GetString()).Should().Equal("pepper");
        notes.GetProperty("base").EnumerateArray().Select(n => n.GetProperty("slug").GetString()).Should().Equal("amber");

        var variants = json.GetProperty("variants").EnumerateArray().ToList();
        variants.Select(v => v.GetProperty("sku").GetString()).Should().Equal("SAU-50", "SAU-100");
        variants[1].GetProperty("price").GetRawText().Should().Be("3200000");
        variants[1].GetProperty("originalPrice").GetRawText().Should().Be("4000000");
        variants[1].GetProperty("discountPercent").GetInt32().Should().Be(20);
        variants[0].GetProperty("originalPrice").ValueKind.Should().Be(JsonValueKind.Null);
        variants[0].GetProperty("discountPercent").ValueKind.Should().Be(JsonValueKind.Null);
        json.GetProperty("seo").GetProperty("metaTitle").GetString().Should().Be("Sauvage – dior | UTperfume");
        json.GetProperty("seo").GetProperty("metaDescription").GetString().Should().Be("Fresh and woody");
    }

    [Fact]
    public async Task Seo_overrides_are_used_when_set()
    {
        var product = NewProduct(NewBrand("b"), "p", variants: NewVariant("P1", 50, 100));
        product.MetaTitle = "Custom title";
        product.MetaDescription = "Custom description";
        await SaveAsync(product);

        var (_, json) = await GetJsonAsync("/api/v1/products/p");

        json.GetProperty("seo").GetProperty("metaTitle").GetString().Should().Be("Custom title");
        json.GetProperty("seo").GetProperty("metaDescription").GetString().Should().Be("Custom description");
    }

    [Fact]
    public async Task Detail_of_a_product_without_images_notes_or_categories_returns_empty_collections()
    {
        await SaveAsync(NewProduct(NewBrand("b"), "bare", variants: NewVariant("B1", 50, 100)));

        var (response, json) = await GetJsonAsync("/api/v1/products/bare");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.GetProperty("images").GetArrayLength().Should().Be(0);
        json.GetProperty("categories").GetArrayLength().Should().Be(0);
        json.GetProperty("notes").GetProperty("top").GetArrayLength().Should().Be(0);
        json.GetProperty("seo").GetProperty("metaDescription").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // AC-P2 (threshold 5, the default)
    [Theory]
    [InlineData(0, "OutOfStock")]
    [InlineData(1, "LowStock")]
    [InlineData(5, "LowStock")]
    [InlineData(6, "InStock")]
    [InlineData(500, "InStock")]
    public async Task Availability_status_follows_the_low_stock_threshold(int stock, string expected)
    {
        await SaveAsync(NewProduct(NewBrand("b"), "p", variants: NewVariant("P1", 50, 100, stock: stock)));

        var (_, json) = await GetJsonAsync("/api/v1/products/p");

        json.GetProperty("variants")[0].GetProperty("availability").GetString().Should().Be(expected);
    }

    [Fact]
    public async Task The_availability_threshold_comes_from_configuration()
    {
        using var factory = new ApiFactory(Database, new Dictionary<string, string?> { ["Catalog:LowStockThreshold"] = "10" });
        using var client = factory.CreateClient();
        await SaveAsync(NewProduct(NewBrand("b"), "p", variants: [NewVariant("P1", 50, 100, stock: 10), NewVariant("P2", 100, 100, stock: 11)]));

        var json = JsonDocument.Parse(await client.GetStringAsync("/api/v1/products/p", Ct)).RootElement;

        json.GetProperty("variants").EnumerateArray().Select(v => v.GetProperty("availability").GetString())
            .Should().Equal("LowStock", "InStock");
    }

    // AC-P2b
    [Fact]
    public async Task Detail_and_related_json_contain_no_quantity_or_blob_path()
    {
        var brand = NewBrand("b");
        var product = NewProduct(brand, "p", variants: NewVariant("P1", 50, 100, stock: 7));
        product.Images.Add(Image("products/1/secret-path.png", 0, primary: true));
        var other = NewProduct(brand, "q", variants: NewVariant("Q1", 50, 100, stock: 3));
        await SaveAsync(product, other);

        foreach (var url in new[] { "/api/v1/products/p", "/api/v1/products/p/related", "/api/v1/products", "/api/v1/brands", "/api/v1/categories" })
        {
            var body = await Client.GetStringAsync(url, Ct);
            body.Should().NotContainEquivalentOf("quantity", url).And.NotContainEquivalentOf("blobPath", url)
                .And.NotContain("secret-path.png\"}", url);
        }

        (await Client.GetStringAsync("/api/v1/products/p", Ct)).Should().Contain("https://images.test/catalog-images/products/1/secret-path.png");
    }

    [Fact]
    public async Task The_generated_openapi_document_has_no_quantity_or_blob_path_property()
    {
        var (response, _) = await GetJsonAsync("/openapi/v1.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var document = await Client.GetStringAsync("/openapi/v1.json", Ct);

        document.Should().NotContainEquivalentOf("quantity").And.NotContainEquivalentOf("blobPath");
        document.Should().Contain("/api/v1/products/{slug}").And.Contain("/api/v1/products/{slug}/related")
            .And.Contain("/api/v1/brands").And.Contain("/api/v1/categories");
    }

    // AC-P3
    [Fact]
    public async Task Inactive_variants_are_not_listed()
    {
        await SaveAsync(NewProduct(NewBrand("b"), "p", variants: [NewVariant("LIVE", 50, 100), NewVariant("DEAD", 100, 100, active: false)]));

        var (_, json) = await GetJsonAsync("/api/v1/products/p");

        json.GetProperty("variants").EnumerateArray().Select(v => v.GetProperty("sku").GetString()).Should().Equal("LIVE");
    }

    // AC-P4
    [Theory]
    [InlineData("draft")]
    [InlineData("archived")]
    [InlineData("inactive-brand")]
    [InlineData("no-active-variant")]
    [InlineData("does-not-exist")]
    public async Task Hidden_or_unknown_products_return_an_identical_404_problem(string slug)
    {
        var active = NewBrand("active");
        var inactive = NewBrand("inactive", active: false);
        await SaveAsync(
            NewProduct(active, "draft", status: ProductStatus.Draft, variants: NewVariant("D1", 50, 100)),
            NewProduct(active, "archived", status: ProductStatus.Archived, variants: NewVariant("A1", 50, 100)),
            NewProduct(inactive, "inactive-brand", variants: NewVariant("I1", 50, 100)),
            NewProduct(active, "no-active-variant", variants: NewVariant("N1", 50, 100, active: false)));

        var (response, json) = await GetJsonAsync($"/api/v1/products/{slug}");

        AssertProblem(response, json, 404);
        json.TryGetProperty("detail", out _).Should().BeFalse("the reason must not be disclosed");
        json.GetProperty("title").GetString().Should().Be("Not Found");
    }

    // AC-P5
    [Theory]
    [InlineData("SAUVAGE")]
    [InlineData("Sauvage")]
    [InlineData("sau%20vage")]
    [InlineData("sau_vage")]
    [InlineData("sauvage.")]
    public async Task Uppercase_or_malformed_slugs_return_404(string slug)
    {
        await SaveAsync(NewProduct(NewBrand("b"), "sauvage", variants: NewVariant("S1", 50, 100)));

        var (response, json) = await GetJsonAsync($"/api/v1/products/{slug}");

        AssertProblem(response, json, 404);
    }

    // AC-R1
    [Fact]
    public async Task Related_ranks_by_shared_categories_then_same_brand_then_newest_and_excludes_the_product_itself()
    {
        var brand = NewBrand("main");
        var otherBrand = NewBrand("other");
        var c1 = NewCategory("c1");
        var c2 = NewCategory("c2");
        var c3 = NewCategory("c3");

        Product Make(Brand b, string slug, DateTime published, params Category[] cats)
        {
            var p = NewProduct(b, slug, publishedAt: published, variants: NewVariant(slug.ToUpperInvariant(), 50, 100));
            p.Categories.AddRange(cats.Select(c => new ProductCategory { Category = c }));
            return p;
        }

        var a = Make(brand, "a", Now.AddDays(-1), c1, c2);
        var bTwoShared = Make(otherBrand, "b-two-shared", Now.AddDays(-50), c1, c2);
        var cOneShared = Make(otherBrand, "c-one-shared", Now.AddDays(-2), c1);
        var dSameBrandNoShared = Make(brand, "d-same-brand", Now.AddDays(-3), c3);
        var eOneSharedNewer = Make(otherBrand, "e-one-shared-newer", Now.AddDays(-1), c2);
        var fUnrelated = Make(otherBrand, "f-unrelated", Now.AddDays(-1), c3);
        await SaveAsync(a, bTwoShared, cOneShared, dSameBrandNoShared, eOneSharedNewer, fUnrelated);

        var (response, json) = await GetJsonAsync("/api/v1/products/a/related");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Slugs(json).Should().Equal("b-two-shared", "e-one-shared-newer", "c-one-shared", "d-same-brand");
        Slugs(json).Should().NotContain("a").And.NotContain("f-unrelated");
    }

    [Fact]
    public async Task Related_excludes_hidden_products_and_inactive_categories_do_not_count()
    {
        var brand = NewBrand("main");
        var inactiveBrand = NewBrand("ib", active: false);
        var shared = NewCategory("shared");
        var retired = NewCategory("retired", active: false);

        Product Make(Brand b, string slug, ProductStatus status, bool activeVariant, Category cat)
        {
            var p = NewProduct(b, slug, status: status, variants: NewVariant(slug.ToUpperInvariant(), 50, 100, active: activeVariant));
            p.Categories.Add(new ProductCategory { Category = cat });
            return p;
        }

        await SaveAsync(
            Make(brand, "current", ProductStatus.Published, true, shared),
            Make(NewBrand("x"), "visible", ProductStatus.Published, true, shared),
            Make(NewBrand("y"), "draft", ProductStatus.Draft, true, shared),
            Make(NewBrand("z"), "no-variant", ProductStatus.Published, false, shared),
            Make(inactiveBrand, "inactive-brand", ProductStatus.Published, true, shared),
            Make(NewBrand("w"), "retired-only", ProductStatus.Published, true, retired));

        var (_, json) = await GetJsonAsync("/api/v1/products/current/related");

        Slugs(json).Should().Equal("visible");
    }

    // AC-R2
    [Theory]
    [InlineData("limit=13")]
    [InlineData("limit=0")]
    [InlineData("limit=-1")]
    public async Task Related_limit_out_of_range_returns_400_keyed_by_limit(string query)
    {
        await SaveAsync(NewProduct(NewBrand("b"), "p", variants: NewVariant("P1", 50, 100)));

        var (response, json) = await GetJsonAsync($"/api/v1/products/p/related?{query}");

        AssertProblem(response, json, 400);
        json.GetProperty("errors").TryGetProperty("limit", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Related_limit_not_a_number_returns_400_keyed_by_limit()
    {
        await SaveAsync(NewProduct(NewBrand("b"), "p", variants: NewVariant("P1", 50, 100)));

        var (response, json) = await GetJsonAsync("/api/v1/products/p/related?limit=lots");

        AssertProblem(response, json, 400);
        json.GetProperty("errors").TryGetProperty("limit", out _).Should().BeTrue();
    }


    [Fact]
    public async Task Related_limit_abc_returns_400_keyed_by_limit()
    {
        await SaveAsync(NewProduct(NewBrand("b"), "p", variants: NewVariant("P1", 50, 100)));

        var (response, json) = await GetJsonAsync("/api/v1/products/p/related?limit=abc");

        AssertProblem(response, json, 400);
        json.GetProperty("errors").TryGetProperty("limit", out _).Should().BeTrue();
    }
    [Fact]
    public async Task Related_defaults_to_at_most_8_and_honours_limit_12()
    {
        var brand = NewBrand("main");
        var current = NewProduct(brand, "current", variants: NewVariant("CUR", 50, 100));
        var others = Enumerable.Range(1, 14)
            .Select(i => NewProduct(brand, $"rel-{i:00}", publishedAt: Now.AddDays(-i), variants: NewVariant($"REL-{i:00}", 50, 100)))
            .ToList<object>();
        await SaveAsync([current, .. others]);

        (await GetJsonAsync("/api/v1/products/current/related")).Json.GetArrayLength().Should().Be(8);
        (await GetJsonAsync("/api/v1/products/current/related?limit=12")).Json.GetArrayLength().Should().Be(12);
        (await GetJsonAsync("/api/v1/products/current/related?limit=1")).Json.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Related_returns_404_for_unknown_hidden_or_malformed_slugs_and_an_empty_array_when_nothing_is_related()
    {
        await SaveAsync(
            NewProduct(NewBrand("b"), "alone", variants: NewVariant("AL", 50, 100)),
            NewProduct(NewBrand("c"), "draft", status: ProductStatus.Draft, variants: NewVariant("DR", 50, 100)));

        foreach (var slug in new[] { "unknown", "draft", "UPPER" })
        {
            var (response, json) = await GetJsonAsync($"/api/v1/products/{slug}/related");
            AssertProblem(response, json, 404);
        }

        var (ok, body) = await GetJsonAsync("/api/v1/products/alone/related");
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        body.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Related_items_use_the_summary_shape_with_integer_vnd_prices()
    {
        var brand = NewBrand("b");
        await SaveAsync(
            NewProduct(brand, "p", variants: NewVariant("P1", 50, 100)),
            NewProduct(brand, "q", variants: NewVariant("Q1", 50, 2_500_000, original: 3_000_000)));

        var (_, json) = await GetJsonAsync("/api/v1/products/p/related");

        json[0].GetProperty("fromPrice").GetRawText().Should().Be("2500000");
        json[0].GetProperty("fromOriginalPrice").GetRawText().Should().Be("3000000");
        json[0].GetProperty("currency").GetString().Should().Be("VND");
    }
}
