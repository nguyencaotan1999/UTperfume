using System.Net;
using System.Text.Json;
using UTperfume.Application.Domain;
using UTperfume.IntegrationTests.Infrastructure;

namespace UTperfume.IntegrationTests.Api;

public sealed class ProductSearchEndpointsTests(TestDatabase database) : CatalogApiTestBase(database)
{
    private static string[] Slugs(JsonElement json) =>
        json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("slug").GetString()!).ToArray();

    private async Task SeedManyAsync(int count)
    {
        var brand = NewBrand("bulk");
        var products = Enumerable.Range(1, count)
            .Select(i => NewProduct(brand, $"bulk-{i:000}", publishedAt: Now.AddMinutes(-i),
                variants: NewVariant($"BULK-{i:000}", 50, 1_000_000 + i)))
            .ToArray<object>();
        await SaveAsync(products);
    }

    // AC-C1
    [Fact]
    public async Task Paging_returns_the_requested_page_with_totals()
    {
        await SeedManyAsync(50);

        var (response, json) = await GetJsonAsync("/api/v1/products?page=2&pageSize=24");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.GetProperty("items").GetArrayLength().Should().Be(24);
        json.GetProperty("page").GetInt32().Should().Be(2);
        json.GetProperty("pageSize").GetInt32().Should().Be(24);
        json.GetProperty("totalItems").GetInt32().Should().Be(50);
        json.GetProperty("totalPages").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task Defaults_are_page_1_size_24_newest_first()
    {
        await SeedManyAsync(30);

        var (_, json) = await GetJsonAsync("/api/v1/products");

        json.GetProperty("page").GetInt32().Should().Be(1);
        json.GetProperty("pageSize").GetInt32().Should().Be(24);
        Slugs(json).Should().HaveCount(24).And.StartWith(["bulk-001", "bulk-002"]);
    }

    // AC-C2
    [Fact]
    public async Task A_page_beyond_the_last_returns_200_with_empty_items_and_correct_totals()
    {
        await SeedManyAsync(50);

        var (response, json) = await GetJsonAsync("/api/v1/products?page=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.GetProperty("items").GetArrayLength().Should().Be(0);
        json.GetProperty("totalItems").GetInt32().Should().Be(50);
        json.GetProperty("totalPages").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task A_huge_page_number_does_not_overflow()
    {
        await SeedManyAsync(3);

        var (response, json) = await GetJsonAsync("/api/v1/products?page=2147483647&pageSize=60");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task An_empty_catalogue_returns_zero_totals()
    {
        var (response, json) = await GetJsonAsync("/api/v1/products");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.GetProperty("totalItems").GetInt32().Should().Be(0);
        json.GetProperty("totalPages").GetInt32().Should().Be(0);
    }

    // AC-C3
    [Theory]
    [InlineData("page=0", "page")]
    [InlineData("page=-1", "page")]
    [InlineData("pageSize=0", "pageSize")]
    [InlineData("pageSize=61", "pageSize")]
    [InlineData("minPrice=-1", "minPrice")]
    [InlineData("maxPrice=10000000001", "maxPrice")]
    [InlineData("minPrice=100&maxPrice=50", "minPrice")]
    [InlineData("sort=cheapest", "sort")]
    [InlineData("sort=PRICE_ASC", "sort")]
    [InlineData("q=a", "q")]
    [InlineData("volume=0", "volume")]
    [InlineData("volume=-5", "volume")]
    [InlineData("volume=5000.1", "volume")]
    [InlineData("brand=BAD_SLUG", "brand")]
    [InlineData("category=Has%20Space", "category")]
    [InlineData("concentration=edp", "concentration")]
    [InlineData("concentration=E", "concentration")]
    public async Task Invalid_parameters_return_400_validation_problem_keyed_by_parameter(string query, string key)
    {
        var (response, json) = await GetJsonAsync($"/api/v1/products?{query}");

        AssertProblem(response, json, 400);
        json.GetProperty("errors").TryGetProperty(key, out var messages).Should().BeTrue($"errors must contain '{key}'");
        messages.GetArrayLength().Should().BeGreaterThan(0);
    }

    // Parsed in code, so the error keys never depend on framework message text.
    [Theory]
    [InlineData("minPrice=1500000.5", "minPrice")]
    [InlineData("page=99999999999", "page")]
    [InlineData("minPrice=1&minPrice=2", "minPrice")]
    [InlineData("volume=1e400", "volume")]
    [InlineData("pageSize=%00", "pageSize")]
    [InlineData("maxPrice=99999999999999999999", "maxPrice")]
    [InlineData("page=%00", "page")]
    public async Task Raw_text_edge_cases_return_400_keyed_by_parameter(string query, string key)
    {
        var (response, json) = await GetJsonAsync($"/api/v1/products?{query}");

        AssertProblem(response, json, 400);
        json.GetProperty("errors").TryGetProperty(key, out var messages).Should().BeTrue($"errors must contain '{key}'");
        messages.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Blank_numeric_parameters_are_treated_as_absent()
    {
        await SeedManyAsync(2);

        var (response, json) = await GetJsonAsync("/api/v1/products?minPrice=&maxPrice=&page=&pageSize=&volume=");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.GetProperty("totalItems").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task A_query_string_over_100_characters_is_rejected()
    {
        var (response, json) = await GetJsonAsync($"/api/v1/products?q={new string('x', 101)}");

        AssertProblem(response, json, 400);
        json.GetProperty("errors").TryGetProperty("q", out _).Should().BeTrue();
    }

    [Fact]
    public async Task More_than_20_values_in_one_filter_is_rejected()
    {
        var query = string.Join('&', Enumerable.Range(1, 21).Select(i => $"brand=b{i}"));

        var (response, json) = await GetJsonAsync($"/api/v1/products?{query}");

        AssertProblem(response, json, 400);
        json.GetProperty("errors").TryGetProperty("brand", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Boundary_values_are_accepted()
    {
        await SeedManyAsync(1);

        foreach (var query in new[]
                 {
                     "pageSize=1", "pageSize=60", "page=1", "minPrice=0", "maxPrice=10000000000", "q=ab",
                     "volume=5000", "volume=0.1", "minPrice=5&maxPrice=5",
                 })
        {
            var (response, _) = await GetJsonAsync($"/api/v1/products?{query}");
            response.StatusCode.Should().Be(HttpStatusCode.OK, query);
        }
    }

    [Fact]
    public async Task A_blank_q_is_treated_as_absent()
    {
        await SeedManyAsync(2);

        var (response, json) = await GetJsonAsync("/api/v1/products?q=%20%20");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.GetProperty("totalItems").GetInt32().Should().Be(2);
    }

    // AC-C4
    [Fact]
    public async Task Min_price_applies_to_the_from_price_not_to_any_variant()
    {
        await SaveAsync(NewProduct(NewBrand("b"), "a", variants: [NewVariant("A-30", 30, 1_500_000), NewVariant("A-100", 100, 3_200_000)]));

        var (_, json) = await GetJsonAsync("/api/v1/products?minPrice=2000000");

        json.GetProperty("totalItems").GetInt32().Should().Be(0);
        var (_, all) = await GetJsonAsync("/api/v1/products");
        all.GetProperty("items")[0].GetProperty("fromPrice").GetInt64().Should().Be(1_500_000);
    }

    // AC-C5
    [Fact]
    public async Task Volume_filter_changes_the_from_price_used_by_the_price_filter()
    {
        await SaveAsync(NewProduct(NewBrand("b"), "a", variants: [NewVariant("A-30", 30, 1_500_000), NewVariant("A-100", 100, 3_200_000)]));

        var (_, json) = await GetJsonAsync("/api/v1/products?volume=100&minPrice=2000000");

        json.GetProperty("totalItems").GetInt32().Should().Be(1);
        json.GetProperty("items")[0].GetProperty("fromPrice").GetInt64().Should().Be(3_200_000);
    }

    [Fact]
    public async Task Max_price_with_a_volume_filter_never_matches_a_product_without_that_volume()
    {
        await SaveAsync(
            NewProduct(NewBrand("b"), "only-30", variants: NewVariant("O-30", 30, 1_000_000)),
            NewProduct(NewBrand("c"), "has-100", variants: NewVariant("H-100", 100, 1_000_000)));

        var (_, json) = await GetJsonAsync("/api/v1/products?volume=100&maxPrice=5000000");

        Slugs(json).Should().Equal("has-100");
    }

    [Fact]
    public async Task Price_bounds_are_inclusive()
    {
        await SaveAsync(NewProduct(NewBrand("b"), "a", variants: NewVariant("A-1", 50, 2_000_000)));

        (await GetJsonAsync("/api/v1/products?minPrice=2000000&maxPrice=2000000")).Json
            .GetProperty("totalItems").GetInt32().Should().Be(1);
        (await GetJsonAsync("/api/v1/products?minPrice=2000001")).Json.GetProperty("totalItems").GetInt32().Should().Be(0);
        (await GetJsonAsync("/api/v1/products?maxPrice=1999999")).Json.GetProperty("totalItems").GetInt32().Should().Be(0);
    }

    // AC-C5b
    [Theory]
    [InlineData("minPrice=1500000.5")]
    [InlineData("maxPrice=1.5")]
    [InlineData("minPrice=abc")]
    [InlineData("page=1.5")]
    [InlineData("pageSize=ten")]
    [InlineData("volume=big")]
    public async Task Non_integer_numbers_return_400_not_500(string query)
    {
        var (response, json) = await GetJsonAsync($"/api/v1/products?{query}");

        AssertProblem(response, json, 400);
        var key = query[..query.IndexOf('=')];
        json.GetProperty("errors").TryGetProperty(key, out _).Should().BeTrue($"errors must name '{key}'");
    }

    // AC-C6
    [Fact]
    public async Task Filters_combine_with_or_within_a_filter_and_and_across_filters()
    {
        var dior = NewBrand("dior");
        var chanel = NewBrand("chanel");
        var other = NewBrand("other");
        await SaveAsync(
            NewProduct(dior, "dior-edp", concentrationId: 3, variants: NewVariant("D1", 50, 100)),
            NewProduct(dior, "dior-edt", concentrationId: 4, variants: NewVariant("D2", 50, 100)),
            NewProduct(chanel, "chanel-edp", concentrationId: 3, variants: NewVariant("C1", 50, 100)),
            NewProduct(other, "other-edp", concentrationId: 3, variants: NewVariant("O1", 50, 100)));

        var (_, json) = await GetJsonAsync("/api/v1/products?brand=dior&brand=chanel&concentration=EDP&sort=name_asc");

        Slugs(json).Should().Equal("chanel-edp", "dior-edp");
    }

    [Fact]
    public async Task Category_and_multiple_concentration_filters_work_and_ignore_inactive_categories()
    {
        var brand = NewBrand("b");
        var men = NewCategory("men");
        var retired = NewCategory("retired", active: false);
        var a = NewProduct(brand, "a", concentrationId: 3, variants: NewVariant("A1", 50, 100));
        a.Categories.Add(new ProductCategory { Category = men });
        var b = NewProduct(brand, "b", concentrationId: 4, variants: NewVariant("B1", 50, 100));
        b.Categories.Add(new ProductCategory { Category = men });
        var c = NewProduct(brand, "c", concentrationId: 3, variants: NewVariant("C1", 50, 100));
        c.Categories.Add(new ProductCategory { Category = retired });
        await SaveAsync(a, b, c);

        Slugs((await GetJsonAsync("/api/v1/products?category=men&sort=name_asc")).Json).Should().Equal("a", "b");
        Slugs((await GetJsonAsync("/api/v1/products?category=men&concentration=EDT")).Json).Should().Equal("b");
        Slugs((await GetJsonAsync("/api/v1/products?concentration=EDP&concentration=EDT&sort=name_asc")).Json).Should().Equal("a", "b", "c");
        (await GetJsonAsync("/api/v1/products?category=retired")).Json.GetProperty("totalItems").GetInt32().Should().Be(0);
    }

    // AC-C7
    [Fact]
    public async Task Unknown_slugs_and_codes_match_nothing_without_an_error()
    {
        await SeedManyAsync(3);

        foreach (var query in new[] { "brand=unknown-brand", "category=unknown", "concentration=ZZZ", "volume=777" })
        {
            var (response, json) = await GetJsonAsync($"/api/v1/products?{query}");
            response.StatusCode.Should().Be(HttpStatusCode.OK, query);
            json.GetProperty("totalItems").GetInt32().Should().Be(0, query);
        }
    }

    // AC-C8
    [Fact]
    public async Task Sorts_are_deterministic_with_id_as_the_tie_breaker_and_pages_never_overlap()
    {
        var brand = NewBrand("b");
        await SaveAsync(
            NewProduct(brand, "p1", name: "Beta", variants: NewVariant("P1", 50, 2_000_000)),
            NewProduct(brand, "p2", name: "Alpha", variants: NewVariant("P2", 50, 1_000_000)),
            NewProduct(brand, "p3", name: "Gamma", variants: NewVariant("P3", 50, 1_000_000)),
            NewProduct(brand, "p4", name: "Alpha", variants: NewVariant("P4", 50, 3_000_000)));

        Slugs((await GetJsonAsync("/api/v1/products?sort=price_asc")).Json).Should().Equal("p2", "p3", "p1", "p4");
        Slugs((await GetJsonAsync("/api/v1/products?sort=price_desc")).Json).Should().Equal("p4", "p1", "p2", "p3");
        Slugs((await GetJsonAsync("/api/v1/products?sort=name_asc")).Json).Should().Equal("p2", "p4", "p1", "p3");
        Slugs((await GetJsonAsync("/api/v1/products?sort=name_desc")).Json).Should().Equal("p3", "p1", "p2", "p4");

        var pageOne = Slugs((await GetJsonAsync("/api/v1/products?sort=price_asc&pageSize=2&page=1")).Json);
        var pageTwo = Slugs((await GetJsonAsync("/api/v1/products?sort=price_asc&pageSize=2&page=2")).Json);
        pageOne.Concat(pageTwo).Should().OnlyHaveUniqueItems().And.HaveCount(4);
    }

    [Fact]
    public async Task Newest_sorts_by_published_date_descending_then_id()
    {
        var brand = NewBrand("b");
        await SaveAsync(
            NewProduct(brand, "old", publishedAt: Now.AddDays(-30), variants: NewVariant("O1", 50, 100)),
            NewProduct(brand, "new", publishedAt: Now.AddDays(-1), variants: NewVariant("N1", 50, 100)),
            NewProduct(brand, "mid", publishedAt: Now.AddDays(-10), variants: NewVariant("M1", 50, 100)));

        Slugs((await GetJsonAsync("/api/v1/products?sort=newest")).Json).Should().Equal("new", "mid", "old");
    }

    // AC-C9
    [Theory]
    [InlineData("dior")]
    [InlineData("DIOR")]
    [InlineData("SAUV")]
    [InlineData("auvag")]
    [InlineData("SAU-EDP-100")]
    [InlineData("sau-edp-100")]
    public async Task Search_matches_product_name_brand_name_and_exact_sku_case_insensitively(string q)
    {
        var dior = NewBrand("dior");
        dior.Name = "Dior";
        await SaveAsync(
            NewProduct(dior, "sauvage", name: "Sauvage", variants: NewVariant("SAU-EDP-100", 100, 100)),
            NewProduct(NewBrand("other"), "unrelated", name: "Other", variants: NewVariant("OTH-1", 50, 100)));

        var (_, json) = await GetJsonAsync($"/api/v1/products?q={q}");

        Slugs(json).Should().Equal("sauvage");
    }

    [Fact]
    public async Task Sku_search_is_exact_and_ignores_inactive_variants()
    {
        await SaveAsync(NewProduct(NewBrand("b"), "p", name: "Nothing", variants:
            [NewVariant("SKU-LIVE", 50, 100), NewVariant("SKU-DEAD", 100, 100, active: false)]));

        (await GetJsonAsync("/api/v1/products?q=SKU-LIVE")).Json.GetProperty("totalItems").GetInt32().Should().Be(1);
        (await GetJsonAsync("/api/v1/products?q=SKU-LIV")).Json.GetProperty("totalItems").GetInt32().Should().Be(0);
        (await GetJsonAsync("/api/v1/products?q=SKU-DEAD")).Json.GetProperty("totalItems").GetInt32().Should().Be(0);
    }

    // AC-C10
    [Theory]
    [InlineData("100%", "p-percent")]
    [InlineData("a_c", "p-underscore")]
    [InlineData("[ab", "p-bracket")]
    public async Task Like_wildcards_in_q_are_matched_literally(string q, string expected)
    {
        var brand = NewBrand("b");
        await SaveAsync(
            NewProduct(brand, "p-percent", name: "Edition 100% Pure", variants: NewVariant("P1", 50, 100)),
            NewProduct(brand, "p-plain", name: "Edition 100 Pure", variants: NewVariant("P2", 50, 100)),
            NewProduct(brand, "p-underscore", name: "Code a_c", variants: NewVariant("P3", 50, 100)),
            NewProduct(brand, "p-abc", name: "Code abc", variants: NewVariant("P4", 50, 100)),
            NewProduct(brand, "p-bracket", name: "Note [ab] Pure", variants: NewVariant("P5", 50, 100)));

        var (response, json) = await GetJsonAsync($"/api/v1/products?q={Uri.EscapeDataString(q)}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Slugs(json).Should().Equal(expected);
    }

    // AC-C11
    [Fact]
    public async Task Hidden_products_are_neither_returned_nor_counted()
    {
        var active = NewBrand("active");
        var inactive = NewBrand("inactive", active: false);
        await SaveAsync(
            NewProduct(active, "visible", variants: NewVariant("V1", 50, 100)),
            NewProduct(active, "draft", status: ProductStatus.Draft, variants: NewVariant("D1", 50, 100)),
            NewProduct(active, "archived", status: ProductStatus.Archived, variants: NewVariant("A1", 50, 100)),
            NewProduct(active, "no-active-variant", variants: NewVariant("N1", 50, 100, active: false)),
            NewProduct(inactive, "inactive-brand", variants: NewVariant("I1", 50, 100)));

        var (_, json) = await GetJsonAsync("/api/v1/products");

        Slugs(json).Should().Equal("visible");
        json.GetProperty("totalItems").GetInt32().Should().Be(1);
    }

    // AC-C12
    [Fact]
    public async Task Item_shows_integer_vnd_prices_compare_at_price_and_currency()
    {
        var brand = NewBrand("dior");
        var product = NewProduct(brand, "sauvage", variants: [NewVariant("S-50", 50, 3_200_000, original: 4_000_000), NewVariant("S-100", 100, 5_000_000)]);
        product.Images.Add(new ProductImage
        {
            BlobPath = "products/1/a.png", ContentType = "image/png", SizeBytes = 1, IsPrimary = true, AltText = "Front", CreatedAtUtc = Now,
        });
        await SaveAsync(product);

        var (_, json) = await GetJsonAsync("/api/v1/products");

        var item = json.GetProperty("items")[0];
        item.GetProperty("fromPrice").ValueKind.Should().Be(JsonValueKind.Number);
        item.GetProperty("fromPrice").GetRawText().Should().Be("3200000");
        item.GetProperty("fromOriginalPrice").GetRawText().Should().Be("4000000");
        item.GetProperty("currency").GetString().Should().Be("VND");
        item.GetProperty("volumesMl").EnumerateArray().Select(v => v.GetDecimal()).Should().Equal(50m, 100m);
        item.GetProperty("inStock").GetBoolean().Should().BeTrue();
        item.GetProperty("primaryImage").GetProperty("url").GetString()
            .Should().Be("https://images.test/catalog-images/products/1/a.png");
        item.GetProperty("primaryImage").GetProperty("altText").GetString().Should().Be("Front");
        item.GetProperty("brand").GetProperty("slug").GetString().Should().Be("dior");
        item.GetProperty("concentration").GetProperty("code").GetString().Should().Be("EDP");
    }

    [Fact]
    public async Task Item_without_an_image_has_a_null_primary_image_and_is_new_follows_the_badge_window()
    {
        var brand = NewBrand("b");
        await SaveAsync(
            NewProduct(brand, "fresh", publishedAt: DateTime.UtcNow.AddDays(-2), variants: NewVariant("F1", 50, 100)),
            NewProduct(brand, "stale", publishedAt: DateTime.UtcNow.AddDays(-90), variants: NewVariant("S1", 50, 100)));

        var (_, json) = await GetJsonAsync("/api/v1/products?sort=newest");

        var items = json.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("slug").GetString()!);
        items["fresh"].GetProperty("isNew").GetBoolean().Should().BeTrue();
        items["stale"].GetProperty("isNew").GetBoolean().Should().BeFalse();
        items["fresh"].GetProperty("primaryImage").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Public_response_has_no_quantity_blob_path_or_admin_fields()
    {
        var product = NewProduct(NewBrand("b"), "p", variants: NewVariant("P1", 50, 100, stock: 7));
        product.Images.Add(new ProductImage { BlobPath = "products/1/secret.png", ContentType = "image/png", SizeBytes = 1, CreatedAtUtc = Now });
        await SaveAsync(product);

        var body = await Client.GetStringAsync("/api/v1/products", Ct);

        body.Should().NotContainEquivalentOf("quantity").And.NotContainEquivalentOf("blobPath")
            .And.NotContain("rowVersion").And.NotContain("status\"");
    }

    [Fact]
    public async Task Search_is_anonymous()
    {
        var response = await Client.GetAsync("/api/v1/products", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
