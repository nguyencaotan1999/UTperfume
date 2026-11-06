using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace UTperfume.Api.Infrastructure;

/// <summary>
/// The numeric query parameters are bound as text and parsed in code (so every error key comes from code, not from
/// framework messages). This transformer documents them with their real types, so the generated contract and the
/// TypeScript client stay accurate (ADR-0009), and declares the sort enum.
/// </summary>
internal sealed class PublicQueryParameterTransformer : IOpenApiOperationTransformer
{
    private static readonly string[] SortValues = ["newest", "price_asc", "price_desc", "name_asc", "name_desc"];

    /// <summary>
    /// Only public operations (<c>api/v1/...</c> but not <c>api/v1/admin/...</c>) get these schemas, so a future admin
    /// parameter that happens to be called <c>page</c> or <c>limit</c> keeps its own definition.
    /// </summary>
    internal static bool IsPublicOperation(string? relativePath)
    {
        var path = "/" + (relativePath ?? string.Empty).TrimStart('/');
        return path.StartsWith("/api/v1/", StringComparison.OrdinalIgnoreCase)
               && !path.Equals("/api/v1/admin", StringComparison.OrdinalIgnoreCase)
               && !path.StartsWith("/api/v1/admin/", StringComparison.OrdinalIgnoreCase);
    }

    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (operation.Parameters is null || !IsPublicOperation(context.Description.RelativePath))
        {
            return Task.CompletedTask;
        }

        foreach (var parameter in operation.Parameters.OfType<OpenApiParameter>())
        {
            switch (parameter.Name)
            {
                case "minPrice" or "maxPrice":
                    parameter.Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int64", Minimum = "0", Maximum = "10000000000" };
                    break;
                case "page":
                    parameter.Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32", Minimum = "1" };
                    break;
                case "pageSize":
                    parameter.Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32", Minimum = "1", Maximum = "60" };
                    break;
                case "limit":
                    parameter.Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32", Minimum = "1", Maximum = "12" };
                    break;
                case "volume":
                    parameter.Schema = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Array,
                        Items = new OpenApiSchema { Type = JsonSchemaType.Number },
                    };
                    break;
                case "sort":
                    parameter.Schema = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Enum = SortValues.Select(v => (System.Text.Json.Nodes.JsonNode)System.Text.Json.Nodes.JsonValue.Create(v)!).ToList(),
                    };
                    break;
            }
        }

        return Task.CompletedTask;
    }
}
