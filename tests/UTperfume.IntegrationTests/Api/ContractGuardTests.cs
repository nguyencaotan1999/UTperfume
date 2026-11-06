using System.Text.Json;
using System.Text.RegularExpressions;
using UTperfume.IntegrationTests.Infrastructure;

namespace UTperfume.IntegrationTests.Api;

/// <summary>
/// P2-08. Fails when the generated OpenAPI document exposes a stock quantity or a blob path anywhere. It scans the
/// document the API actually generates (every <c>properties</c> object at any depth, and every parameter name), so a
/// new endpoint or schema is covered automatically; there is no hard-coded list of schemas to forget (BR-28, AC-P2b).
/// </summary>
public sealed class ContractGuardTests(TestDatabase database) : CatalogApiTestBase(database)
{
    private static readonly Regex Forbidden = new(
        @"(?i)(^stockQuantity$|quantity$|blobPath)", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    /// <summary>Returns the JSON path of every property or parameter whose name is forbidden, and how many names were checked.</summary>
    internal static (List<string> Violations, int NamesChecked) Scan(JsonElement document)
    {
        var violations = new List<string>();
        var checkedNames = 0;
        Walk(document, "$", violations, ref checkedNames);
        return (violations, checkedNames);
    }

    private static void Walk(JsonElement node, string path, List<string> violations, ref int checkedNames)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var member in node.EnumerateObject())
                {
                    if (member.Name == "properties" && member.Value.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var property in member.Value.EnumerateObject())
                        {
                            checkedNames++;
                            if (Forbidden.IsMatch(property.Name))
                            {
                                violations.Add($"{path}.properties.{property.Name}");
                            }
                        }
                    }

                    if (member.Name == "name" && member.Value.ValueKind == JsonValueKind.String
                        && node.TryGetProperty("in", out _))
                    {
                        checkedNames++;
                        if (Forbidden.IsMatch(member.Value.GetString()!))
                        {
                            violations.Add($"{path} parameter '{member.Value.GetString()}'");
                        }
                    }

                    Walk(member.Value, $"{path}.{member.Name}", violations, ref checkedNames);
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in node.EnumerateArray())
                {
                    Walk(item, $"{path}[{index++}]", violations, ref checkedNames);
                }

                break;
        }
    }

    [Fact]
    public async Task The_generated_public_openapi_document_has_no_quantity_or_blob_path_property_or_parameter()
    {
        var (response, document) = await GetJsonAsync("/openapi/v1.json");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var (violations, namesChecked) = Scan(document);

        namesChecked.Should().BeGreaterThan(50, "the guard must actually have walked the generated schemas, not an empty document");
        document.GetProperty("components").GetProperty("schemas").EnumerateObject().Should().NotBeEmpty();
        violations.Should().BeEmpty("public contracts must never expose stock quantities or blob paths");
    }

    [Fact]
    public async Task The_committed_openapi_file_matches_the_same_rule()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "openapi", "v1.json")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("openapi/v1.json is committed at the repository root");
        using var committed = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory!.FullName, "openapi", "v1.json"), Ct));

        var (violations, namesChecked) = Scan(committed.RootElement);

        namesChecked.Should().BeGreaterThan(50);
        violations.Should().BeEmpty();
    }

    // Proof that the guard is not vacuous: it flags each forbidden shape wherever it appears.
    [Theory]
    [InlineData("""{"components":{"schemas":{"X":{"properties":{"stockQuantity":{"type":"integer"}}}}}}""")]
    [InlineData("""{"components":{"schemas":{"X":{"properties":{"reservedQuantity":{"type":"integer"}}}}}}""")]
    [InlineData("""{"components":{"schemas":{"X":{"properties":{"Quantity":{"type":"integer"}}}}}}""")]
    [InlineData("""{"components":{"schemas":{"X":{"properties":{"blobPath":{"type":"string"}}}}}}""")]
    [InlineData("""{"components":{"schemas":{"X":{"properties":{"logoBlobPath":{"type":"string"}}}}}}""")]
    [InlineData("""{"paths":{"/a":{"get":{"responses":{"200":{"content":{"application/json":{"schema":{"type":"object","properties":{"stockQuantity":{}}}}}}}}}}}""")]
    [InlineData("""{"components":{"schemas":{"X":{"allOf":[{"properties":{"inner":{"properties":{"stockQuantity":{}}}}}]}}}}""")]
    [InlineData("""{"paths":{"/a":{"get":{"parameters":[{"name":"stockQuantity","in":"query"}]}}}}""")]
    public void The_guard_detects_forbidden_names_at_any_depth(string json)
    {
        using var document = JsonDocument.Parse(json);

        var (violations, _) = Scan(document.RootElement);

        violations.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("""{"components":{"schemas":{"X":{"properties":{"fromPrice":{},"productCount":{},"quantityNote":{}}}}}}""")]
    [InlineData("""{"paths":{"/a":{"get":{"parameters":[{"name":"page","in":"query"}]}}}}""")]
    public void The_guard_accepts_ordinary_names(string json)
    {
        using var document = JsonDocument.Parse(json);

        var (violations, namesChecked) = Scan(document.RootElement);

        violations.Should().BeEmpty();
        namesChecked.Should().BeGreaterThan(0);
    }
}
