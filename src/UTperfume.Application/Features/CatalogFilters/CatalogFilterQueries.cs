using Microsoft.EntityFrameworkCore;
using UTperfume.Application.Common;

namespace UTperfume.Application.Features.CatalogFilters;

/// <summary>Whole-VND from-price bounds over visible products.</summary>
public sealed record PriceRange(long Min, long Max);

/// <summary>E9. Every list contains only values that at least one visible product uses (BR-01, BR-02).</summary>
public sealed record CatalogFilters(
    IReadOnlyList<BrandRef> Brands,
    IReadOnlyList<CategoryRef> Categories,
    IReadOnlyList<ConcentrationRef> Concentrations,
    IReadOnlyList<decimal> VolumesMl,
    PriceRange? PriceRange,
    IReadOnlyList<string> SortOptions)
{
    public string Currency => Money.Currency;
}

public sealed class CatalogFilterQueries(ICatalogDbContext db)
{
    public async Task<CatalogFilters> GetAsync(CancellationToken cancellationToken)
    {
        var visible = db.Products.AsNoTracking().VisibleProducts();

        var brandIds = await visible.Select(p => p.BrandId).Distinct().ToListAsync(cancellationToken);
        var brands = await db.Brands.AsNoTracking()
            .Where(b => b.IsActive && brandIds.Contains(b.Id))
            .OrderBy(b => b.Name).ThenBy(b => b.Id)
            .Select(b => new BrandRef(b.Slug, b.Name))
            .ToListAsync(cancellationToken);

        // An inactive category is never offered as a filter option (BR-02).
        var categoryIds = await visible.SelectMany(p => p.Categories).Select(c => c.CategoryId).Distinct()
            .ToListAsync(cancellationToken);
        var categories = await db.Categories.AsNoTracking()
            .Where(c => c.IsActive && categoryIds.Contains(c.Id))
            .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name).ThenBy(c => c.Id)
            .Select(c => new CategoryRef(c.Slug, c.Name))
            .ToListAsync(cancellationToken);

        var concentrationIds = await visible.Select(p => p.ConcentrationId).Distinct().ToListAsync(cancellationToken);
        var concentrations = await db.Concentrations.AsNoTracking()
            .Where(c => c.IsActive && concentrationIds.Contains(c.Id))
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
            .Select(c => new ConcentrationRef(c.Code, c.Name))
            .ToListAsync(cancellationToken);

        var volumes = await visible.SelectMany(p => p.Variants)
            .Where(v => v.IsActive)
            .Select(v => v.VolumeMl)
            .Distinct()
            .OrderBy(v => v)
            .ToListAsync(cancellationToken);

        // Bounds of the per-product from-price (the same value the listing shows), not of single variants.
        var fromPrices = visible.Select(p => p.Variants.Where(v => v.IsActive).Min(v => (long?)v.Price));
        var min = await fromPrices.MinAsync(cancellationToken);
        var max = await fromPrices.MaxAsync(cancellationToken);

        return new CatalogFilters(
            brands,
            categories,
            concentrations,
            volumes,
            min is { } lowest && max is { } highest ? new PriceRange(lowest, highest) : null,
            ProductSearchValidatorSortOptions());
    }

    private static IReadOnlyList<string> ProductSearchValidatorSortOptions() => Products.ProductSearchValidator.SortValues;
}
