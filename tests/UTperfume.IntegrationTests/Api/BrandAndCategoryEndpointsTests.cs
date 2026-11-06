using System.Net;
using UTperfume.Application.Domain;
using UTperfume.IntegrationTests.Infrastructure;

namespace UTperfume.IntegrationTests.Api;

public sealed class BrandEndpointsTests(TestDatabase database) : CatalogApiTestBase(database)
{
    // AC-B1
    [Fact]
    public async Task List_returns_active_brands_ordered_by_name_with_visible_product_counts()
    {
        var zeta = NewBrand("zeta");
        var alpha = NewBrand("alpha", logo: "brands/alpha.png");
        var empty = NewBrand("empty");
        var inactive = NewBrand("inactive", active: false);
        await SaveAsync(
            NewProduct(zeta, "z-1", variants: NewVariant("Z1", 50, 100)),
            NewProduct(alpha, "a-visible", variants: NewVariant("A1", 50, 100)),
            NewProduct(alpha, "a-visible-2", variants: [NewVariant("A2", 50, 100), NewVariant("A2B", 100, 100, active: false)]),
            NewProduct(alpha, "a-draft", status: ProductStatus.Draft, variants: NewVariant("A3", 50, 100)),
            NewProduct(alpha, "a-no-active-variant", variants: NewVariant("A4", 50, 100, active: false)),
            NewProduct(inactive, "i-1", variants: NewVariant("I1", 50, 100)),
            empty);

        var (response, json) = await GetJsonAsync("/api/v1/brands");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.EnumerateArray().Select(b => b.GetProperty("slug").GetString())
            .Should().Equal("alpha", "empty", "zeta");
        var counts = json.EnumerateArray().ToDictionary(b => b.GetProperty("slug").GetString()!, b => b.GetProperty("productCount").GetInt32());
        counts.Should().Contain("alpha", 2).And.Contain("empty", 0).And.Contain("zeta", 1);
        json.EnumerateArray().First().GetProperty("logoUrl").GetString()
            .Should().Be("https://images.test/catalog-images/brands/alpha.png");
        json.EnumerateArray().Skip(1).First().GetProperty("logoUrl").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        json.GetRawText().Should().NotContain("blobPath").And.NotContain("logoBlobPath");
    }

    [Fact]
    public async Task List_is_anonymous_and_empty_catalogue_returns_an_empty_array()
    {
        var (response, json) = await GetJsonAsync("/api/v1/brands");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.GetArrayLength().Should().Be(0);
    }

    // AC-B2
    [Fact]
    public async Task Get_returns_200_for_an_active_brand()
    {
        var brand = NewBrand("dior", logo: "brands/dior.png");
        brand.Description = "House of Dior";
        await SaveAsync(NewProduct(brand, "sauvage", variants: NewVariant("S1", 50, 100)));

        var (response, json) = await GetJsonAsync("/api/v1/brands/dior");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.GetProperty("slug").GetString().Should().Be("dior");
        json.GetProperty("description").GetString().Should().Be("House of Dior");
        json.GetProperty("productCount").GetInt32().Should().Be(1);
        json.GetProperty("logoUrl").GetString().Should().Be("https://images.test/catalog-images/brands/dior.png");
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("inactive")]
    [InlineData("UPPER")]
    [InlineData("bad_slug")]
    [InlineData("-x")]
    public async Task Get_returns_404_problem_details_for_unknown_inactive_or_malformed_slugs(string slug)
    {
        await SaveAsync(NewBrand("inactive", active: false));

        var (response, json) = await GetJsonAsync($"/api/v1/brands/{slug}");

        AssertProblem(response, json, 404);
    }

    [Fact]
    public async Task Wrong_method_is_405_with_allow()
    {
        var response = await Client.PostAsync("/api/v1/brands", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        response.Content.Headers.Allow.Should().Contain("GET");
    }
}

public sealed class CategoryEndpointsTests(TestDatabase database) : CatalogApiTestBase(database)
{
    // AC-K1
    [Fact]
    public async Task List_orders_by_display_order_then_name_and_counts_visible_products()
    {
        var brand = NewBrand("b");
        var inactiveBrand = NewBrand("ib", active: false);
        var men = NewCategory("men", order: 1);
        var women = NewCategory("women", order: 0);
        var alpha = NewCategory("alpha", order: 1);
        var retired = NewCategory("retired", active: false, order: 0);
        var hiddenProduct = NewProduct(inactiveBrand, "hidden", variants: NewVariant("H1", 50, 100));
        hiddenProduct.Categories.Add(new ProductCategory { Category = men });
        var p1 = NewProduct(brand, "p1", variants: NewVariant("P1", 50, 100));
        p1.Categories.AddRange([new ProductCategory { Category = men }, new ProductCategory { Category = retired }]);
        var p2 = NewProduct(brand, "p2", variants: NewVariant("P2", 50, 100));
        p2.Categories.Add(new ProductCategory { Category = women });
        await SaveAsync(p1, p2, hiddenProduct, alpha);

        var (response, json) = await GetJsonAsync("/api/v1/categories");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.EnumerateArray().Select(c => c.GetProperty("slug").GetString()).Should().Equal("women", "alpha", "men");
        var counts = json.EnumerateArray().ToDictionary(c => c.GetProperty("slug").GetString()!, c => c.GetProperty("productCount").GetInt32());
        counts.Should().Contain("women", 1).And.Contain("alpha", 0).And.Contain("men", 1);
    }

    [Fact]
    public async Task Image_url_is_built_from_the_blob_path()
    {
        await SaveAsync(NewCategory("men", image: "categories/men.png"));

        var (_, json) = await GetJsonAsync("/api/v1/categories");

        json[0].GetProperty("imageUrl").GetString().Should().Be("https://images.test/catalog-images/categories/men.png");
    }

    [Fact]
    public async Task Get_returns_200_for_active_and_404_problem_details_for_inactive_unknown_or_malformed()
    {
        var brand = NewBrand("b");
        var men = NewCategory("men");
        var product = NewProduct(brand, "p1", variants: NewVariant("P1", 50, 100));
        product.Categories.Add(new ProductCategory { Category = men });
        await SaveAsync(product, NewCategory("retired", active: false));

        var (ok, okJson) = await GetJsonAsync("/api/v1/categories/men");
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        okJson.GetProperty("productCount").GetInt32().Should().Be(1);

        foreach (var slug in new[] { "retired", "unknown", "Men", "a_b" })
        {
            var (response, json) = await GetJsonAsync($"/api/v1/categories/{slug}");
            AssertProblem(response, json, 404);
        }
    }
}
