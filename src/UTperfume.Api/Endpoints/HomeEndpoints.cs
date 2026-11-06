using UTperfume.Application.Features.Home;

namespace UTperfume.Api.Endpoints;

internal static class HomeEndpoints
{
    public static RouteGroupBuilder MapHomeEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/home", async (HomeQueries queries, CancellationToken ct) =>
                TypedResults.Ok(await queries.GetAsync(ct)))
            .WithName("GetHome")
            .WithTags("Home")
            .Produces<HomeResponse>();

        return group;
    }
}
