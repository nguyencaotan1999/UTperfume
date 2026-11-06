using UTperfume.Application.Domain;

namespace UTperfume.Application.Features.Products;

/// <summary>
/// Public product summary row. It deliberately has no quantity member: the stock quantity never leaves the
/// database for public queries (BR-08, BR-28). Only the derived status and the in-stock flag are projected.
/// </summary>
public sealed record ProductSummaryRow(
    int Id,
    string Slug,
    string Name,
    string BrandSlug,
    string BrandName,
    string ConcentrationCode,
    string ConcentrationName,
    string? PrimaryImageBlobPath,
    string? PrimaryImageAltText,
    long FromPrice,
    long? FromOriginalPrice,
    IReadOnlyList<decimal> VolumesMl,
    bool InStock,
    DateTime? PublishedAtUtc);

/// <summary>Public variant row with the derived availability status and no quantity member.</summary>
public sealed record VariantAvailabilityRow(
    int ProductId,
    string Sku,
    decimal VolumeMl,
    long Price,
    long? OriginalPrice,
    AvailabilityStatus Availability);

public static class ProductSummaryProjection
{
    /// <summary>
    /// Projects products to <see cref="ProductSummaryRow"/> in a single SQL statement. Callers start from
    /// <c>CatalogVisibility.VisibleProducts()</c>.
    /// </summary>
    /// <param name="volumesMl">
    /// When set, products without an active variant in this volume set are excluded, and the from-price and
    /// compare-at price are taken only from the variants in the set (BR-11, BR-13).
    /// </param>
    public static IQueryable<ProductSummaryRow> ToSummaryRows(
        this IQueryable<Product> products, IReadOnlyCollection<decimal>? volumesMl = null)
    {
        ArgumentNullException.ThrowIfNull(products);

        // Two explicit shapes (instead of one with a null test inside) keep the SQL simple and parameter-friendly.
        if (volumesMl is { Count: > 0 })
        {
            var volumes = volumesMl.ToArray();
            return products
                .Where(p => p.Variants.Any(v => v.IsActive && volumes.Contains(v.VolumeMl)))
                .Select(p => new ProductSummaryRow(
                p.Id,
                p.Slug,
                p.Name,
                p.Brand.Slug,
                p.Brand.Name,
                p.Concentration.Code,
                p.Concentration.Name,
                p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.SortOrder).ThenBy(i => i.Id)
                    .Select(i => i.BlobPath).FirstOrDefault(),
                p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.SortOrder).ThenBy(i => i.Id)
                    .Select(i => i.AltText).FirstOrDefault(),
                p.Variants.Where(v => v.IsActive && volumes.Contains(v.VolumeMl)).Min(v => (long?)v.Price) ?? 0,
                p.Variants.Where(v => v.IsActive && volumes.Contains(v.VolumeMl))
                    .OrderBy(v => v.Price).ThenBy(v => v.VolumeMl).Select(v => v.OriginalPrice).FirstOrDefault(),
                p.Variants.Where(v => v.IsActive).OrderBy(v => v.VolumeMl).Select(v => v.VolumeMl).ToList(),
                p.Variants.Any(v => v.IsActive && v.StockQuantity > 0),
                p.PublishedAtUtc));
        }

        return products.Select(p => new ProductSummaryRow(
            p.Id,
            p.Slug,
            p.Name,
            p.Brand.Slug,
            p.Brand.Name,
            p.Concentration.Code,
            p.Concentration.Name,
            p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.SortOrder).ThenBy(i => i.Id)
                .Select(i => i.BlobPath).FirstOrDefault(),
            p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.SortOrder).ThenBy(i => i.Id)
                .Select(i => i.AltText).FirstOrDefault(),
            p.Variants.Where(v => v.IsActive).Min(v => (long?)v.Price) ?? 0,
            p.Variants.Where(v => v.IsActive)
                .OrderBy(v => v.Price).ThenBy(v => v.VolumeMl).Select(v => v.OriginalPrice).FirstOrDefault(),
            p.Variants.Where(v => v.IsActive).OrderBy(v => v.VolumeMl).Select(v => v.VolumeMl).ToList(),
            p.Variants.Any(v => v.IsActive && v.StockQuantity > 0),
            p.PublishedAtUtc));
    }

    /// <summary>
    /// Projects active variants with the availability computed in SQL as a <c>CASE</c> over the threshold
    /// parameter (ADR-0005). Keep this expression in step with <see cref="Availability.From"/>; a test asserts parity.
    /// </summary>
    public static IQueryable<VariantAvailabilityRow> ToAvailabilityRows(
        this IQueryable<ProductVariant> variants, int lowStockThreshold)
    {
        ArgumentNullException.ThrowIfNull(variants);
        ArgumentOutOfRangeException.ThrowIfLessThan(lowStockThreshold, 1);

        return variants
            .Where(v => v.IsActive)
            .Select(v => new VariantAvailabilityRow(
                v.ProductId,
                v.Sku,
                v.VolumeMl,
                v.Price,
                v.OriginalPrice,
                v.StockQuantity <= 0 ? AvailabilityStatus.OutOfStock
                    : v.StockQuantity <= lowStockThreshold ? AvailabilityStatus.LowStock
                    : AvailabilityStatus.InStock));
    }
}

/// <summary>
/// Price filter and price sort on the visibility-filtered product query. The from-price expression is the same one
/// used by <see cref="ProductSummaryProjection.ToSummaryRows"/> (BR-11, BR-12): the minimum price over active variants,
/// restricted to the selected volumes when a volume filter is applied. EF cannot translate predicates over the
/// projected record, so the expression is repeated here at entity level and covered by tests.
/// </summary>
public static class ProductPriceQueries
{
    public static IQueryable<Product> WherePriceBetween(
        this IQueryable<Product> products, long? minPrice, long? maxPrice, IReadOnlyCollection<decimal>? volumesMl)
    {
        ArgumentNullException.ThrowIfNull(products);

        if (volumesMl is { Count: > 0 })
        {
            var volumes = volumesMl.ToArray();
            if (minPrice is { } min)
            {
                products = products.Where(p => p.Variants.Where(v => v.IsActive && volumes.Contains(v.VolumeMl)).Min(v => (long?)v.Price) >= min);
            }

            if (maxPrice is { } max)
            {
                products = products.Where(p => p.Variants.Where(v => v.IsActive && volumes.Contains(v.VolumeMl)).Min(v => (long?)v.Price) <= max);
            }

            return products;
        }

        if (minPrice is { } minAll)
        {
            products = products.Where(p => p.Variants.Where(v => v.IsActive).Min(v => (long?)v.Price) >= minAll);
        }

        if (maxPrice is { } maxAll)
        {
            products = products.Where(p => p.Variants.Where(v => v.IsActive).Min(v => (long?)v.Price) <= maxAll);
        }

        return products;
    }

    public static IQueryable<Product> OrderByFromPrice(
        this IQueryable<Product> products, bool descending, IReadOnlyCollection<decimal>? volumesMl)
    {
        ArgumentNullException.ThrowIfNull(products);

        if (volumesMl is { Count: > 0 })
        {
            var volumes = volumesMl.ToArray();
            return descending
                ? products.OrderByDescending(p => p.Variants.Where(v => v.IsActive && volumes.Contains(v.VolumeMl)).Min(v => (long?)v.Price)).ThenBy(p => p.Id)
                : products.OrderBy(p => p.Variants.Where(v => v.IsActive && volumes.Contains(v.VolumeMl)).Min(v => (long?)v.Price)).ThenBy(p => p.Id);
        }

        return descending
            ? products.OrderByDescending(p => p.Variants.Where(v => v.IsActive).Min(v => (long?)v.Price)).ThenBy(p => p.Id)
            : products.OrderBy(p => p.Variants.Where(v => v.IsActive).Min(v => (long?)v.Price)).ThenBy(p => p.Id);
    }
}
