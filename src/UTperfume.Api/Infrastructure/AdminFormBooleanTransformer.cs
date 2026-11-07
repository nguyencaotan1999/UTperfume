using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace UTperfume.Api.Infrastructure;

/// <summary>
/// The admin upload binds <c>isPrimary</c> as text and parses it in code, so a value that is not a boolean is answered
/// with a 400 keyed by <c>isPrimary</c> rather than a framework message. This transformer documents the multipart field
/// with its real type (boolean), so the generated contract and the TypeScript client stay accurate (ADR-0009).
/// </summary>
internal sealed class AdminFormBooleanTransformer : IOpenApiOperationTransformer
{
    private const string FieldName = "isPrimary";

    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (operation.RequestBody?.Content is null
            || !operation.RequestBody.Content.TryGetValue("multipart/form-data", out var media)
            || media.Schema is null
            || PublicQueryParameterTransformer.IsPublicOperation(context.Description.RelativePath))
        {
            return Task.CompletedTask;
        }

        Replace(media.Schema);
        return Task.CompletedTask;
    }

    private static void Replace(IOpenApiSchema schema)
    {
        if (schema.Properties is { } properties && properties.ContainsKey(FieldName))
        {
            properties[FieldName] = new OpenApiSchema { Type = JsonSchemaType.Boolean };
        }

        if (schema.AllOf is { } parts)
        {
            foreach (var part in parts)
            {
                Replace(part);
            }
        }
    }
}
