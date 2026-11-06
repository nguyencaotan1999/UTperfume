using Microsoft.EntityFrameworkCore;
using UTperfume.Application.Common;

namespace UTperfume.Application.Features.Brands;

public sealed record BrandSummary(string Slug, string Name, string? LogoUrl, int ProductCount);

public sealed record BrandDetail(string Slug, string Name, string? Description, string? LogoUrl, int ProductCount);

/// <summary>E5 and E6. Only active brands are public (BR-02); counts use the shared visibility rule (BR-01).</summary>
public sealed class BrandQueries(ICatalogDbContext db, IImageStorage storage)
{
    public async Task<IReadOnlyList<BrandSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var counts = await db.Products.AsNoTracking().VisibleProducts()
            .GroupBy(p => p.BrandId)
            .Select(g => new { BrandId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BrandId, x => x.Count, cancellationToken);

        var brands = await db.Brands.AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name).ThenBy(b => b.Id)
            .Select(b => new { b.Id, b.Slug, b.Name, b.LogoBlobPath })
            .ToListAsync(cancellationToken);

        return brands
            .Select(b => new BrandSummary(b.Slug, b.Name, UrlOrNull(b.LogoBlobPath), counts.GetValueOrDefault(b.Id)))
            .ToList();
    }

    /// <summary>BR-21: active, featured brands with at least one visible product, by display order then name.</summary>
    public async Task<IReadOnlyList<BrandSummary>> PopularAsync(int max, CancellationToken cancellationToken)
    {
        var counts = await db.Products.AsNoTracking().VisibleProducts()
            .GroupBy(p => p.BrandId)
            .Select(g => new { BrandId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BrandId, x => x.Count, cancellationToken);

        var brands = await db.Brands.AsNoTracking()
            .Where(b => b.IsActive && b.IsFeatured && counts.Keys.Contains(b.Id))
            .OrderBy(b => b.DisplayOrder).ThenBy(b => b.Name).ThenBy(b => b.Id)
            .Take(max)
            .Select(b => new { b.Id, b.Slug, b.Name, b.LogoBlobPath })
            .ToListAsync(cancellationToken);

        return brands.Select(b => new BrandSummary(b.Slug, b.Name, UrlOrNull(b.LogoBlobPath), counts[b.Id])).ToList();
    }

    public async Task<Result<BrandDetail>> GetAsync(string slug, CancellationToken cancellationToken)
    {
        if (!Slug.IsValid(slug))
        {
            return Result<BrandDetail>.NotFound();
        }

        var brand = await db.Brands.AsNoTracking()
            .Where(b => b.IsActive && b.Slug == slug)
            .Select(b => new { b.Id, b.Slug, b.Name, b.Description, b.LogoBlobPath })
            .SingleOrDefaultAsync(cancellationToken);
        if (brand is null)
        {
            return Result<BrandDetail>.NotFound();
        }

        var count = await db.Products.AsNoTracking().VisibleProducts().CountAsync(p => p.BrandId == brand.Id, cancellationToken);
        return Result<BrandDetail>.Ok(new BrandDetail(brand.Slug, brand.Name, brand.Description, UrlOrNull(brand.LogoBlobPath), count));
    }

    private string? UrlOrNull(string? blobPath) => blobPath is null ? null : storage.GetPublicUrl(blobPath);
}
