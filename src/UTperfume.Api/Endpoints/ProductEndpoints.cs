using Microsoft.AspNetCore.Mvc;
using UTperfume.Api.Infrastructure;
using UTperfume.Application.Common;
using UTperfume.Application.Features.Products;

namespace UTperfume.Api.Endpoints;

/// <summary>Query string of E2. Property names are the public parameter names (camel-cased by the binder key).</summary>
internal sealed record ProductSearchQuery(
    [FromQuery(Name = "q")] string? Q,
    [FromQuery(Name = "brand")] string[]? Brand,
    [FromQuery(Name = "category")] string[]? Category,
    [FromQuery(Name = "concentration")] string[]? Concentration,
    [FromQuery(Name = "volume")] string[]? Volume,
    [FromQuery(Name = "minPrice")] string? MinPrice,
    [FromQuery(Name = "maxPrice")] string? MaxPrice,
    [FromQuery(Name = "sort")] string? Sort,
    [FromQuery(Name = "page")] string? Page,
    [FromQuery(Name = "pageSize")] string? PageSize)
{
    public ProductSearchRequest ToRequest() =>
        new(Q, Brand, Category, Concentration, Volume, MinPrice, MaxPrice, Sort, Page, PageSize);
}

internal static class ProductEndpoints
{
    public static RouteGroupBuilder MapProductEndpoints(this RouteGroupBuilder group)
    {
        var products = group.MapGroup("/products").WithTags("Products");

        products.MapGet("/", async ([AsParameters] ProductSearchQuery query, ProductSearchHandler handler, CancellationToken ct) =>
                (await handler.SearchAsync(query.ToRequest(), ct)).ToHttpResult())
            .WithName("SearchProducts")
            .Produces<PagedResult<ProductSummary>>()
            .ProducesValidationProblem();

        products.MapGet("/{slug}", async (string slug, ProductDetailHandler handler, CancellationToken ct) =>
                (await handler.GetAsync(slug, ct)).ToHttpResult())
            .WithName("GetProduct")
            .Produces<ProductDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        products.MapGet("/{slug}/related", async (
                string slug, [FromQuery(Name = "limit")] string? limit, ProductDetailHandler handler, CancellationToken ct) =>
                (await handler.RelatedAsync(slug, limit, ct)).ToHttpResult())
            .WithName("GetRelatedProducts")
            .Produces<IReadOnlyList<ProductSummary>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }
}
