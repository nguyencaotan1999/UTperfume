using System.Net;
using System.Text.Json;
using UTperfume.Application.Domain;
using UTperfume.IntegrationTests.Infrastructure;

namespace UTperfume.IntegrationTests.Api;

public sealed class CatalogFilterEndpointsTests(TestDatabase database) : CatalogApiTestBase(database)
{
    private static string[] Strings(JsonElement array, string property) =>
        array.EnumerateArray().Select(e => e.GetProperty(property).GetString()!).ToArray();

    // AC-F1
    [Fact]
    public async Task Filters_list_only_values_used_by_visible_products()
    {
        var dior = NewBrand("dior");
        var chanel = NewBrand("chanel");
        var unusedBrand = NewBrand("unused");
        var inactiveBrand = NewBrand("inactive", active: false);
        var men = NewCategory("men");
        var women = NewCategory("women");
        var unusedCategory = NewCategory("unused-cat");
        var retired = NewCategory("retired", active: false);

        var p1 = NewProduct(dior, "p1", concentrationId: 3, variants: [NewVariant("P1-50", 50, 2_000_000), NewVariant("P1-100", 100, 3_000_000)]);
        p1.Categories.Add(new ProductCategory { Category = men });
        var p2 = NewProduct(chanel, "p2", concentrationId: 4, variants: NewVariant("P2-30", 30, 1_500_000));
        p2.Categories.AddRange([new ProductCategory { Category = women }, new ProductCategory { Category = retired }]);
        // None of these may leak into any list.
        var hiddenDraft = NewProduct(unusedBrand, "draft", concentrationId: 1, status: ProductStatus.Draft, variants: NewVariant("D1", 200, 100));
        hiddenDraft.Categories.Add(new ProductCategory { Category = unusedCategory });
        var hiddenBrand = NewProduct(inactiveBrand, "ib", concentrationId: 2, variants: NewVariant("I1", 75, 50));
        var noActive = NewProduct(unusedBrand, "noactive", concentrationId: 5, variants: NewVariant("N1", 150, 40, active: false));
        await SaveAsync(p1, p2, hiddenDraft, hiddenBrand, noActive);

        var (response, json) = await GetJsonAsync("/api/v1/catalog/filters");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Strings(json.GetProperty("brands"), "slug").Should().Equal("chanel", "dior");
        Strings(json.GetProperty("categories"), "slug").Should().Equal("men", "women");
        Strings(json.GetProperty("concentrations"), "code").Should().Equal("EDP", "EDT");
        json.GetProperty("volumesMl").EnumerateArray().Select(v => v.GetDecimal()).Should().Equal(30m, 50m, 100m);
        json.GetProperty("currency").GetString().Should().Be("VND");
    }

    [Fact]
    public async Task Price_range_is_over_the_per_product_from_price_as_integers()
    {
        var brand = NewBrand("b");
        await SaveAsync(
            NewProduct(brand, "cheap", variants: [NewVariant("C1", 30, 1_500_000), NewVariant("C2", 100, 9_000_000)]),
            NewProduct(brand, "dear", variants: [NewVariant("D1", 50, 4_200_000), NewVariant("D2", 100, 8_900_000)]),
            NewProduct(brand, "hidden-cheaper", status: ProductStatus.Draft, variants: NewVariant("H1", 50, 10_000)),
            NewProduct(brand, "inactive-variant-cheaper", variants: [NewVariant("V1", 50, 3_000_000), NewVariant("V2", 100, 5_000, active: false)]));

        var (_, json) = await GetJsonAsync("/api/v1/catalog/filters");

        var range = json.GetProperty("priceRange");
        range.GetProperty("min").GetRawText().Should().Be("1500000");
        range.GetProperty("max").GetRawText().Should().Be("4200000", "the maximum is the highest from-price, not the highest variant price");
    }

    [Fact]
    public async Task Price_range_is_null_and_lists_are_empty_when_there_are_no_visible_products()
    {
        await SaveAsync(NewProduct(NewBrand("b"), "draft", status: ProductStatus.Draft, variants: NewVariant("D1", 50, 100)));

        var (response, json) = await GetJsonAsync("/api/v1/catalog/filters");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.GetProperty("priceRange").ValueKind.Should().Be(JsonValueKind.Null);
        foreach (var name in new[] { "brands", "categories", "concentrations", "volumesMl" })
        {
            json.GetProperty(name).GetArrayLength().Should().Be(0, name);
        }
    }

    [Fact]
    public async Task Sort_options_list_the_five_sort_values()
    {
        var (_, json) = await GetJsonAsync("/api/v1/catalog/filters");

        json.GetProperty("sortOptions").EnumerateArray().Select(s => s.GetString())
            .Should().Equal("newest", "price_asc", "price_desc", "name_asc", "name_desc");
    }

    [Fact]
    public async Task Concentrations_follow_the_lookup_sort_order()
    {
        var brand = NewBrand("b");
        await SaveAsync(
            NewProduct(brand, "edt", concentrationId: 4, variants: NewVariant("T1", 50, 100)),
            NewProduct(brand, "parfum", concentrationId: 1, variants: NewVariant("P1", 50, 100)),
            NewProduct(brand, "edp", concentrationId: 3, variants: NewVariant("E1", 50, 100)));

        var (_, json) = await GetJsonAsync("/api/v1/catalog/filters");

        Strings(json.GetProperty("concentrations"), "code").Should().Equal("PARFUM", "EDP", "EDT");
    }

    [Fact]
    public async Task Response_is_anonymous_and_has_no_quantity_blob_path_or_ids()
    {
        await SaveAsync(NewProduct(NewBrand("b"), "p", variants: NewVariant("P1", 50, 100, stock: 9)));

        var body = await Client.GetStringAsync("/api/v1/catalog/filters", Ct);

        body.Should().NotContainEquivalentOf("quantity").And.NotContainEquivalentOf("blobPath").And.NotContain("\"id\"");
    }

    [Fact]
    public async Task Filters_route_does_not_collide_with_a_product_slug_lookup()
    {
        await SaveAsync(NewProduct(NewBrand("b"), "filters", variants: NewVariant("F1", 50, 100)));

        var (response, json) = await GetJsonAsync("/api/v1/products/filters");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.GetProperty("slug").GetString().Should().Be("filters");
    }
}
