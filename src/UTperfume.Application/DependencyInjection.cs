using Microsoft.Extensions.DependencyInjection;
using UTperfume.Application.Features.Brands;
using UTperfume.Application.Features.CatalogFilters;
using UTperfume.Application.Features.Home;
using UTperfume.Application.Features.Sitemap;
using UTperfume.Application.Features.Categories;
using UTperfume.Application.Features.Products;

namespace UTperfume.Application;

public static class DependencyInjection
{
    /// <summary>Registers the query handlers. They are stateless and scoped to the request's DbContext.</summary>
    public static IServiceCollection AddCatalogFeatures(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<BrandQueries>();
        services.AddScoped<CategoryQueries>();
        services.AddScoped<ProductSearchHandler>();
        services.AddScoped<ProductDetailHandler>();
        services.AddScoped<CatalogFilterQueries>();
        services.AddScoped<HomeQueries>();
        services.AddScoped<SitemapQueries>();
        return services;
    }
}
