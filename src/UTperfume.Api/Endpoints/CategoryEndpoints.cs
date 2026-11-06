using UTperfume.Api.Infrastructure;
using UTperfume.Application.Features.Categories;

namespace UTperfume.Api.Endpoints;

internal static class CategoryEndpoints
{
    public static RouteGroupBuilder MapCategoryEndpoints(this RouteGroupBuilder group)
    {
        var categories = group.MapGroup("/categories").WithTags("Categories");

        categories.MapGet("/", async (CategoryQueries queries, CancellationToken ct) =>
                TypedResults.Ok(await queries.ListAsync(ct)))
            .WithName("ListCategories")
            .Produces<IReadOnlyList<CategorySummary>>();

        categories.MapGet("/{slug}", async (string slug, CategoryQueries queries, CancellationToken ct) =>
                (await queries.GetAsync(slug, ct)).ToHttpResult())
            .WithName("GetCategory")
            .Produces<CategoryDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }
}
