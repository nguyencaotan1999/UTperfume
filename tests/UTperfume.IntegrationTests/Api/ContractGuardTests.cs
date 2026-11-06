using System.Text.Json;
using System.Text.RegularExpressions;
using UTperfume.IntegrationTests.Infrastructure;

namespace UTperfume.IntegrationTests.Api;

/// <summary>
/// P2-08, scoped by P4-01. Fails when the generated OpenAPI document exposes a blob path anywhere, or a stock quantity
/// on a public path (everything under <c>/api/v1</c> except <c>/api/v1/admin</c>, plus the schemas those paths
/// reference). It scans the document the API actually generates (every <c>properties</c> object at any depth, and every
/// parameter name), so a new endpoint or schema is covered automatically; there is no hard-coded list of schemas to
/// forget (BR-28, AC-P2b).
/// </summary>
public sealed class ContractGuardTests(TestDatabase database) : CatalogApiTestBase(database)
{
    // Global rule: no path, parameter or schema anywhere (public or admin) may expose a blob path.
    private static readonly Regex BlobPath = new(
        @"(?i)blobPath", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    // Public-only rule: public contracts never expose stock quantities. Admin contracts (/api/v1/admin) legitimately
    // carry stockQuantity (0002), so this rule is applied only to what the public paths can reach.
    private static readonly Regex Quantity = new(
        @"(?i)(^stockQuantity$|quantity$)", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private const string AdminPrefix = "/api/v1/admin";
    private const string RefPrefix = "#/";

    private static bool IsAdminPath(string path) =>
        path.Equals(AdminPrefix, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(AdminPrefix + "/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the JSON path of every violation and how many names were checked by the public scope. The blob path
    /// rule scans the whole document. The quantity rule scans every non-admin path item plus every component reachable
    /// from it through <c>$ref</c>, so admin-only schemas are exempt but a schema shared with a public path is still
    /// checked.
    /// </summary>
    internal static (List<string> Violations, int NamesChecked) Scan(JsonElement document)
    {
        var violations = new List<string>();
        var ignored = 0;
        Walk(document, "$", BlobPath, violations, ref ignored, null);

        var checkedNames = 0;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>();

        if (document.ValueKind == JsonValueKind.Object
            && document.TryGetProperty("paths", out var paths) && paths.ValueKind == JsonValueKind.Object)
        {
            foreach (var item in paths.EnumerateObject())
            {
                if (IsAdminPath(item.Name))
                {
                    continue;
                }

                var refs = new List<string>();
                Walk(item.Value, $"$.paths.{item.Name}", Quantity, violations, ref checkedNames, refs);
                refs.ForEach(pending.Enqueue);
            }
        }

        while (pending.Count > 0)
        {
            var reference = pending.Dequeue();
            if (!reference.StartsWith(RefPrefix, StringComparison.Ordinal) || !visited.Add(reference))
            {
                continue;
            }

            var target = document;
            var found = true;
            foreach (var segment in reference[RefPrefix.Length..].Split('/'))
            {
                if (target.ValueKind == JsonValueKind.Object && target.TryGetProperty(segment, out var next))
                {
                    target = next;
                }
                else
                {
                    found = false;
                    break;
                }
            }

            if (!found)
            {
                violations.Add($"unresolved $ref '{reference}' reachable from a public path");
                continue;
            }

            var refsFromTarget = new List<string>();
            Walk(target, "$." + reference[RefPrefix.Length..].Replace('/', '.'), Quantity, violations, ref checkedNames, refsFromTarget);
            refsFromTarget.ForEach(pending.Enqueue);
        }

        return (violations, checkedNames);
    }

    private static void Walk(
        JsonElement node, string path, Regex forbidden, List<string> violations, ref int checkedNames, List<string>? refs)
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
                            if (forbidden.IsMatch(property.Name))
                            {
                                violations.Add($"{path}.properties.{property.Name}");
                            }
                        }
                    }

                    if (member.Name == "name" && member.Value.ValueKind == JsonValueKind.String
                        && node.TryGetProperty("in", out _))
                    {
                        checkedNames++;
                        if (forbidden.IsMatch(member.Value.GetString()!))
                        {
                            violations.Add($"{path} parameter '{member.Value.GetString()}'");
                        }
                    }

                    if (member.Name == "$ref" && member.Value.ValueKind == JsonValueKind.String)
                    {
                        refs?.Add(member.Value.GetString()!);
                    }

                    Walk(member.Value, $"{path}.{member.Name}", forbidden, violations, ref checkedNames, refs);
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in node.EnumerateArray())
                {
                    Walk(item, $"{path}[{index++}]", forbidden, violations, ref checkedNames, refs);
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

    private static string PublicSchema(string schema) =>
        """{"paths":{"/api/v1/x":{"get":{"schema":{"$ref":"#/components/schemas/X"}}}},"components":{"schemas":{"X":""" + schema + "}}}";

    // Proof that the guard is not vacuous: it flags each forbidden shape wherever it appears on a public path.
    public static TheoryData<string> ForbiddenPublicShapes() => new()
    {
        PublicSchema("""{"properties":{"stockQuantity":{"type":"integer"}}}"""),
        PublicSchema("""{"properties":{"reservedQuantity":{"type":"integer"}}}"""),
        PublicSchema("""{"properties":{"Quantity":{"type":"integer"}}}"""),
        PublicSchema("""{"properties":{"blobPath":{"type":"string"}}}"""),
        PublicSchema("""{"properties":{"logoBlobPath":{"type":"string"}}}"""),
        PublicSchema("""{"allOf":[{"properties":{"inner":{"properties":{"stockQuantity":{}}}}}]}"""),
        """{"paths":{"/api/v1/a":{"get":{"responses":{"200":{"content":{"application/json":{"schema":{"type":"object","properties":{"stockQuantity":{}}}}}}}}}}}""",
        """{"paths":{"/api/v1/a":{"get":{"parameters":[{"name":"stockQuantity","in":"query"}]}}}}""",
        // A schema shared by a public and an admin path stays checked.
        """{"paths":{"/api/v1/a":{"get":{"schema":{"$ref":"#/components/schemas/X"}}},"/api/v1/admin/a":{"get":{"schema":{"$ref":"#/components/schemas/X"}}}},"components":{"schemas":{"X":{"properties":{"stockQuantity":{}}}}}}""",
        // Nested reference chain from a public path.
        """{"paths":{"/api/v1/a":{"get":{"schema":{"$ref":"#/components/schemas/X"}}}},"components":{"schemas":{"X":{"properties":{"y":{"$ref":"#/components/schemas/Y"}}},"Y":{"properties":{"stockQuantity":{}}}}}}""",
        // A path that merely resembles the admin prefix is public.
        """{"paths":{"/api/v1/administrators":{"get":{"parameters":[{"name":"stockQuantity","in":"query"}]}}}}""",
    };

    [Theory]
    [MemberData(nameof(ForbiddenPublicShapes))]
    public void The_guard_detects_forbidden_names_at_any_depth(string json)
    {
        using var document = JsonDocument.Parse(json);

        var (violations, _) = Scan(document.RootElement);

        violations.Should().NotBeEmpty();
    }

    // Blob paths are forbidden everywhere, including admin paths and unreferenced schemas.
    [Theory]
    [InlineData("""{"paths":{"/api/v1/admin/a":{"get":{"schema":{"properties":{"blobPath":{}}}}}}}""")]
    [InlineData("""{"components":{"schemas":{"AdminOnly":{"properties":{"imageBlobPath":{}}}}}}""")]
    [InlineData("""{"paths":{"/api/v1/admin/a":{"get":{"parameters":[{"name":"blobPath","in":"query"}]}}}}""")]
    public void The_guard_detects_blob_paths_even_outside_public_paths(string json)
    {
        using var document = JsonDocument.Parse(json);

        var (violations, _) = Scan(document.RootElement);

        violations.Should().NotBeEmpty();
    }

    // Admin contracts may carry stock quantities; only public paths are held to the quantity rule.
    [Theory]
    [InlineData("""{"paths":{"/api/v1/admin/products":{"get":{"schema":{"$ref":"#/components/schemas/AdminProduct"}}},"/api/v1/admin":{"get":{}}},"components":{"schemas":{"AdminProduct":{"properties":{"stockQuantity":{"type":"integer"}}}}}}""")]
    [InlineData("""{"paths":{"/api/v1/admin/products":{"get":{"parameters":[{"name":"stockQuantity","in":"query"}]}}}}""")]
    public void The_guard_allows_stock_quantity_on_admin_paths(string json)
    {
        using var document = JsonDocument.Parse(json);

        var (violations, _) = Scan(document.RootElement);

        violations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("""{"paths":{"/api/v1/a":{"get":{"schema":{"$ref":"#/components/schemas/X"}}}},"components":{"schemas":{"X":{"properties":{"fromPrice":{},"productCount":{},"quantityNote":{}}}}}}""")]
    [InlineData("""{"paths":{"/api/v1/a":{"get":{"parameters":[{"name":"page","in":"query"}]}}}}""")]
    public void The_guard_accepts_ordinary_names(string json)
    {
        using var document = JsonDocument.Parse(json);

        var (violations, namesChecked) = Scan(document.RootElement);

        violations.Should().BeEmpty();
        namesChecked.Should().BeGreaterThan(0);
    }
}
