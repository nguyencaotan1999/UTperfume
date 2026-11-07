using System.Text.Json;
using UTperfume.IntegrationTests.Infrastructure;

namespace UTperfume.IntegrationTests.Api;

/// <summary>Checks the generated OpenAPI document, which feeds the TypeScript client (ADR-0009).</summary>
public sealed class OpenApiContractTests(TestDatabase database) : CatalogApiTestBase(database)
{
    private async Task<JsonElement> DocumentAsync() => (await GetJsonAsync("/openapi/v1.json")).Json;

    private static JsonElement Schema(JsonElement document, string name) =>
        document.GetProperty("components").GetProperty("schemas").GetProperty(name);

    private static string[] TypesOf(JsonElement property) =>
        property.GetProperty("type").ValueKind == JsonValueKind.Array
            ? property.GetProperty("type").EnumerateArray().Select(t => t.GetString()!).Order().ToArray()
            : [property.GetProperty("type").GetString()!];

    [Theory]
    [InlineData("ProductSummary", "fromPrice", false)]
    [InlineData("ProductSummary", "fromOriginalPrice", true)]
    [InlineData("VariantDto", "price", false)]
    [InlineData("VariantDto", "originalPrice", true)]
    public async Task Money_fields_are_integer_int64_and_never_a_string_or_number_union(string schema, string property, bool nullable)
    {
        var field = Schema(await DocumentAsync(), schema).GetProperty("properties").GetProperty(property);

        field.GetProperty("format").GetString().Should().Be("int64");
        TypesOf(field).Should().Equal(nullable ? ["integer", "null"] : ["integer"]);
        field.TryGetProperty("pattern", out _).Should().BeFalse("no string pattern means the type is not 'integer or string'");
    }

    [Theory]
    [InlineData("BrandSummary", "productCount")]
    [InlineData("BrandDetail", "productCount")]
    [InlineData("CategorySummary", "productCount")]
    [InlineData("CategoryDetail", "productCount")]
    public async Task Product_counts_are_plain_integers(string schema, string property)
    {
        var field = Schema(await DocumentAsync(), schema).GetProperty("properties").GetProperty(property);

        TypesOf(field).Should().Equal("integer");
        field.GetProperty("format").GetString().Should().Be("int32");
        field.TryGetProperty("pattern", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Paged_result_totals_are_plain_integers()
    {
        var document = await DocumentAsync();
        var paged = document.GetProperty("components").GetProperty("schemas").EnumerateObject()
            .Single(s => s.Name.StartsWith("PagedResult", StringComparison.Ordinal)).Value;

        foreach (var name in new[] { "page", "pageSize", "totalItems", "totalPages" })
        {
            var field = paged.GetProperty("properties").GetProperty(name);
            TypesOf(field).Should().Equal("integer");
            field.TryGetProperty("pattern", out _).Should().BeFalse(name);
        }
    }

    [Fact]
    public async Task No_schema_anywhere_uses_an_integer_or_string_union()
    {
        var text = (await DocumentAsync()).GetRawText();

        text.Should().NotContain("\"pattern\":\"^-?(?:0|[1-9]");
        text.Should().NotMatchRegex("\"type\":\\[\"(integer|number)\",\"string\"\\]");
        text.Should().NotMatchRegex("\"type\":\\[\"string\",\"(integer|number)\"\\]");
    }

    [Fact]
    public async Task Query_parameters_are_documented_with_their_real_types_and_the_sort_enum()
    {
        var document = await DocumentAsync();
        var parameters = document.GetProperty("paths").GetProperty("/api/v1/products").GetProperty("get")
            .GetProperty("parameters").EnumerateArray().ToDictionary(p => p.GetProperty("name").GetString()!);

        foreach (var name in new[] { "minPrice", "maxPrice" })
        {
            parameters[name].GetProperty("schema").GetProperty("type").GetString().Should().Be("integer");
            parameters[name].GetProperty("schema").GetProperty("format").GetString().Should().Be("int64");
        }

        parameters["page"].GetProperty("schema").GetProperty("type").GetString().Should().Be("integer");
        parameters["pageSize"].GetProperty("schema").GetProperty("type").GetString().Should().Be("integer");
        parameters["volume"].GetProperty("schema").GetProperty("type").GetString().Should().Be("array");
        parameters["volume"].GetProperty("schema").GetProperty("items").GetProperty("type").GetString().Should().Be("number");
        parameters["sort"].GetProperty("schema").GetProperty("enum").EnumerateArray().Select(v => v.GetString())
            .Should().Equal("newest", "price_asc", "price_desc", "name_asc", "name_desc");

        var related = document.GetProperty("paths").GetProperty("/api/v1/products/{slug}/related").GetProperty("get")
            .GetProperty("parameters").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "limit");
        related.GetProperty("schema").GetProperty("type").GetString().Should().Be("integer");
    }

    private async Task<JsonElement> UploadOperationAsync() =>
        (await DocumentAsync()).GetProperty("paths").GetProperty("/api/v1/admin/products/{id}/images").GetProperty("post");

    [Fact]
    public async Task Admin_upload_documents_isPrimary_as_a_boolean_in_the_multipart_schema()
    {
        var schema = (await UploadOperationAsync()).GetProperty("requestBody").GetProperty("content")
            .GetProperty("multipart/form-data").GetProperty("schema");

        var isPrimary = schema.GetProperty("allOf").EnumerateArray()
            .Select(part => part.GetProperty("properties"))
            .Single(props => props.TryGetProperty("isPrimary", out _))
            .GetProperty("isPrimary");

        isPrimary.GetProperty("type").GetString().Should().Be("boolean");
    }

    [Fact]
    public async Task Admin_upload_declares_the_503_storage_unavailable_response()
    {
        var responses = (await UploadOperationAsync()).GetProperty("responses");

        responses.TryGetProperty("503", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Strict_number_handling_keeps_every_public_money_value_a_json_number()
    {
        var brand = NewBrand("b");
        await SaveAsync(NewProduct(brand, "p", variants: NewVariant("P1", 50, 3_200_000, original: 4_000_000)));

        var detail = (await GetJsonAsync("/api/v1/products/p")).Json;
        var list = (await GetJsonAsync("/api/v1/products")).Json;
        var brands = (await GetJsonAsync("/api/v1/brands")).Json;

        detail.GetProperty("variants")[0].GetProperty("price").ValueKind.Should().Be(JsonValueKind.Number);
        detail.GetProperty("variants")[0].GetProperty("originalPrice").ValueKind.Should().Be(JsonValueKind.Number);
        detail.GetProperty("variants")[0].GetProperty("volumeMl").ValueKind.Should().Be(JsonValueKind.Number);
        list.GetProperty("items")[0].GetProperty("fromPrice").ValueKind.Should().Be(JsonValueKind.Number);
        list.GetProperty("totalItems").ValueKind.Should().Be(JsonValueKind.Number);
        brands[0].GetProperty("productCount").ValueKind.Should().Be(JsonValueKind.Number);
    }
}
