using UTperfume.Api.Infrastructure;
using UTperfume.Application.Features.Brands;

namespace UTperfume.Api.Endpoints;

internal static class BrandEndpoints
{
    public static RouteGroupBuilder MapBrandEndpoints(this RouteGroupBuilder group)
    {
        var brands = group.MapGroup("/brands").WithTags("Brands");

        brands.MapGet("/", async (BrandQueries queries, CancellationToken ct) =>
                TypedResults.Ok(await queries.ListAsync(ct)))
            .WithName("ListBrands")
            .Produces<IReadOnlyList<BrandSummary>>();

        brands.MapGet("/{slug}", async (string slug, BrandQueries queries, CancellationToken ct) =>
                (await queries.GetAsync(slug, ct)).ToHttpResult())
            .WithName("GetBrand")
            .Produces<BrandDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }
}
