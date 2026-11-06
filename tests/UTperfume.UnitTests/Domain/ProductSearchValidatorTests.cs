using UTperfume.Application.Features.Products;

namespace UTperfume.UnitTests.Domain;

public class ProductSearchValidatorTests
{
    private static ProductSearchRequest Req(
        string? q = null, string[]? brand = null, string[]? category = null, string[]? concentration = null,
        decimal[]? volume = null, long? min = null, long? max = null, string? sort = null, int? page = null, int? pageSize = null) =>
        new(q, brand, category, concentration, volume?.Select(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray(),
            min?.ToString(System.Globalization.CultureInfo.InvariantCulture), max?.ToString(System.Globalization.CultureInfo.InvariantCulture), sort,
            page?.ToString(System.Globalization.CultureInfo.InvariantCulture), pageSize?.ToString(System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void Defaults_are_page_1_size_24_newest()
    {
        var result = ProductSearchValidator.Validate(Req());

        result.IsSuccess.Should().BeTrue();
        result.Value!.Page.Should().Be(1);
        result.Value.PageSize.Should().Be(24);
        result.Value.Sort.Should().Be(ProductSort.Newest);
        result.Value.Q.Should().BeNull();
    }

    [Fact]
    public void Q_is_trimmed_and_blank_means_absent()
    {
        ProductSearchValidator.Validate(Req(q: "  dior ")).Value!.Q.Should().Be("dior");
        ProductSearchValidator.Validate(Req(q: "   ")).Value!.Q.Should().BeNull();
    }

    [Theory]
    [InlineData("a", false)]
    [InlineData("ab", true)]
    public void Q_minimum_length_is_two(string q, bool valid) =>
        ProductSearchValidator.Validate(Req(q: q)).IsSuccess.Should().Be(valid);

    [Fact]
    public void Q_maximum_length_is_100()
    {
        ProductSearchValidator.Validate(Req(q: new string('x', 100))).IsSuccess.Should().BeTrue();
        ProductSearchValidator.Validate(Req(q: new string('x', 101))).IsSuccess.Should().BeFalse();
    }

    [Theory]
    [InlineData(-1L, null, "minPrice")]
    [InlineData(null, -1L, "maxPrice")]
    [InlineData(null, 10_000_000_001L, "maxPrice")]
    [InlineData(100L, 50L, "minPrice")]
    public void Price_rules_report_the_offending_parameter(long? min, long? max, string key)
    {
        var result = ProductSearchValidator.Validate(Req(min: min, max: max));

        result.IsSuccess.Should().BeFalse();
        result.Error!.Details.Should().ContainKey(key);
    }

    [Fact]
    public void Price_boundaries_are_accepted()
    {
        ProductSearchValidator.Validate(Req(min: 0, max: 10_000_000_000)).IsSuccess.Should().BeTrue();
        ProductSearchValidator.Validate(Req(min: 5, max: 5)).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("newest", ProductSort.Newest)]
    [InlineData("price_asc", ProductSort.PriceAsc)]
    [InlineData("price_desc", ProductSort.PriceDesc)]
    [InlineData("name_asc", ProductSort.NameAsc)]
    [InlineData("name_desc", ProductSort.NameDesc)]
    public void All_five_sorts_are_accepted(string sort, ProductSort expected) =>
        ProductSearchValidator.Validate(Req(sort: sort)).Value!.Sort.Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("Newest")]
    [InlineData("cheapest")]
    public void Unknown_sorts_are_rejected(string sort) =>
        ProductSearchValidator.Validate(Req(sort: sort)).Error!.Details.Should().ContainKey("sort");

    [Theory]
    [InlineData(0, null, "page")]
    [InlineData(-5, null, "page")]
    [InlineData(null, 0, "pageSize")]
    [InlineData(null, 61, "pageSize")]
    public void Paging_bounds_are_enforced(int? page, int? pageSize, string key) =>
        ProductSearchValidator.Validate(Req(page: page, pageSize: pageSize)).Error!.Details.Should().ContainKey(key);

    [Fact]
    public void Reports_every_error_at_once()
    {
        var result = ProductSearchValidator.Validate(Req(q: "a", sort: "x", page: 0, pageSize: 99));

        result.Error!.Details!.Keys.Should().BeEquivalentTo(["q", "sort", "page", "pageSize"]);
    }

    [Fact]
    public void Blank_filter_values_are_dropped_and_duplicates_collapsed()
    {
        var result = ProductSearchValidator.Validate(Req(brand: ["dior", " ", "dior", ""]));

        result.Value!.Brands.Should().Equal("dior");
    }

    [Fact]
    public void Filters_accept_at_most_twenty_values()
    {
        var twenty = Enumerable.Range(1, 20).Select(i => $"b{i}").ToArray();
        var twentyOne = Enumerable.Range(1, 21).Select(i => $"b{i}").ToArray();

        ProductSearchValidator.Validate(Req(brand: twenty)).IsSuccess.Should().BeTrue();
        ProductSearchValidator.Validate(Req(brand: twentyOne)).Error!.Details.Should().ContainKey("brand");
    }

    [Theory]
    [InlineData("EDP", true)]
    [InlineData("EAU_FRAICHE", true)]
    [InlineData("edp", false)]
    [InlineData("E", false)]
    [InlineData("EDP2", false)]
    public void Concentration_codes_are_upper_case(string code, bool valid) =>
        ProductSearchValidator.Validate(Req(concentration: [code])).IsSuccess.Should().Be(valid);

    [Fact]
    public void Volume_rules_are_greater_than_zero_and_at_most_5000()
    {
        ProductSearchValidator.Validate(Req(volume: [0.1m, 5000m])).IsSuccess.Should().BeTrue();
        ProductSearchValidator.Validate(Req(volume: [0m])).Error!.Details.Should().ContainKey("volume");
        ProductSearchValidator.Validate(Req(volume: [5000.1m])).Error!.Details.Should().ContainKey("volume");
    }
}

public class ProductSearchRawNumberTests
{
    private static ProductSearchRequest Raw(
        string[]? volume = null, string? min = null, string? max = null, string? page = null, string? pageSize = null) =>
        new(null, null, null, null, volume, min, max, null, page, pageSize);

    [Theory]
    [InlineData("minPrice", "1500000.5")]
    [InlineData("minPrice", "abc")]
    [InlineData("minPrice", "1,5")]
    [InlineData("minPrice", "1e3")]
    [InlineData("minPrice", "99999999999999999999")]
    [InlineData("minPrice", "1,2")]
    [InlineData("maxPrice", "1.5")]
    [InlineData("page", "99999999999")]
    [InlineData("page", "1.5")]
    [InlineData("page", "\0")]
    [InlineData("pageSize", "\0")]
    [InlineData("pageSize", "ten")]
    public void Non_integer_or_out_of_range_text_is_an_error_keyed_by_the_parameter(string key, string value)
    {
        var request = key switch
        {
            "minPrice" => Raw(min: value),
            "maxPrice" => Raw(max: value),
            "page" => Raw(page: value),
            _ => Raw(pageSize: value),
        };

        var result = ProductSearchValidator.Validate(request);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Details.Should().ContainKey(key);
    }

    [Theory]
    [InlineData("1e400")]
    [InlineData("big")]
    [InlineData("1,5")]
    [InlineData("0")]
    [InlineData("5000.1")]
    public void Bad_volume_text_is_an_error_keyed_by_volume(string value)
    {
        var result = ProductSearchValidator.Validate(Raw(volume: [value]));

        result.Error!.Details.Should().ContainKey("volume");
    }

    [Fact]
    public void Valid_numbers_parse_with_the_invariant_culture()
    {
        var result = ProductSearchValidator.Validate(Raw(volume: ["100", "30.5"], min: "1250000", max: " 8900000 ", page: "2", pageSize: "60"));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Volumes.Should().Equal(100m, 30.5m);
        result.Value.MinPrice.Should().Be(1_250_000);
        result.Value.MaxPrice.Should().Be(8_900_000);
        result.Value.Page.Should().Be(2);
        result.Value.PageSize.Should().Be(60);
    }

    [Fact]
    public void Blank_numbers_count_as_absent()
    {
        var result = ProductSearchValidator.Validate(Raw(min: "", max: "  ", page: "", pageSize: " "));

        result.IsSuccess.Should().BeTrue();
        result.Value!.MinPrice.Should().BeNull();
        result.Value.Page.Should().Be(1);
        result.Value.PageSize.Should().Be(24);
    }

    [Fact]
    public void A_repeated_value_joined_by_the_binder_is_rejected()
    {
        // The binder joins a repeated minPrice into "1,2".
        ProductSearchValidator.Validate(Raw(min: "1,2")).Error!.Details.Should().ContainKey("minPrice");
    }
}
