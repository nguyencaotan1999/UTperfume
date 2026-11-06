using UTperfume.Application.Features.CatalogFilters;

namespace UTperfume.Api.Endpoints;

internal static class CatalogFilterEndpoints
{
    public static RouteGroupBuilder MapCatalogFilterEndpoints(this RouteGroupBuilder group)
    {
        // Under /catalog so it cannot collide with /products/{slug}.
        group.MapGet("/catalog/filters", async (CatalogFilterQueries queries, CancellationToken ct) =>
                TypedResults.Ok(await queries.GetAsync(ct)))
            .WithName("GetCatalogFilters")
            .WithTags("Catalog")
            .Produces<CatalogFilters>();

        return group;
    }
}
