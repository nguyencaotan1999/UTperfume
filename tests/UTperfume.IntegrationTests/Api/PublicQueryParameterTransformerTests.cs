using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using UTperfume.Api.Infrastructure;

namespace UTperfume.IntegrationTests.Api;

public class PublicQueryParameterTransformerTests
{
    private static async Task<OpenApiOperation> RunAsync(string relativePath)
    {
        var operation = new OpenApiOperation
        {
            Parameters =
            [
                new OpenApiParameter { Name = "page", In = ParameterLocation.Query, Schema = new OpenApiSchema { Type = JsonSchemaType.String } },
                new OpenApiParameter { Name = "limit", In = ParameterLocation.Query, Schema = new OpenApiSchema { Type = JsonSchemaType.String } },
            ],
        };
        var context = new OpenApiOperationTransformerContext
        {
            DocumentName = "v1",
            Description = new ApiDescription { RelativePath = relativePath },
            ApplicationServices = new ServiceCollection().BuildServiceProvider(),
        };

        await new PublicQueryParameterTransformer().TransformAsync(operation, context, CancellationToken.None);
        return operation;
    }

    private static JsonSchemaType? TypeOf(OpenApiOperation operation, string name) =>
        operation.Parameters!.Single(p => p.Name == name).Schema!.Type;

    [Theory]
    [InlineData("api/v1/products")]
    [InlineData("api/v1/products/{slug}/related")]
    [InlineData("/api/v1/brands")]
    public async Task Public_operations_get_the_documented_integer_schemas(string path)
    {
        var operation = await RunAsync(path);

        TypeOf(operation, "page").Should().Be(JsonSchemaType.Integer);
        TypeOf(operation, "limit").Should().Be(JsonSchemaType.Integer);
    }

    [Theory]
    [InlineData("api/v1/admin/products")]
    [InlineData("api/v1/admin")]
    [InlineData("/api/v1/admin/brands/{id}")]
    [InlineData("api/v1/ADMIN/things")]
    [InlineData("sitemap.xml")]
    [InlineData("health/ready")]
    public async Task Operations_outside_the_public_group_keep_their_own_schema(string path)
    {
        var operation = await RunAsync(path);

        TypeOf(operation, "page").Should().Be(JsonSchemaType.String);
        TypeOf(operation, "limit").Should().Be(JsonSchemaType.String);
    }

    [Theory]
    [InlineData("api/v1/products", true)]
    [InlineData("api/v1/administrators", true)]
    [InlineData("api/v1/admin", false)]
    [InlineData("api/v1/admin/x", false)]
    [InlineData("api/v2/products", false)]
    [InlineData(null, false)]
    public void IsPublicOperation_matches_only_public_v1_paths(string? path, bool expected)
    {
        PublicQueryParameterTransformer.IsPublicOperation(path).Should().Be(expected);
    }
}
