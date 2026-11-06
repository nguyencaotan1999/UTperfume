using UTperfume.Application.Domain;

namespace UTperfume.Application.Common;

/// <summary>The single implementation of public visibility (BR-01), reused by every public query.</summary>
public static class CatalogVisibility
{
    /// <summary>
    /// Published, with an active brand and at least one active variant. Everything else is "not found" publicly:
    /// listings, home sections, related products, filter options and the sitemap must start from this query.
    /// </summary>
    public static IQueryable<Product> VisibleProducts(this IQueryable<Product> products)
    {
        ArgumentNullException.ThrowIfNull(products);

        return products.Where(p =>
            p.Status == ProductStatus.Published
            && p.Brand.IsActive
            && p.Variants.Any(v => v.IsActive));
    }
}
