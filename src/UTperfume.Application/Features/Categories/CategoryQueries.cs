using Microsoft.EntityFrameworkCore;
using UTperfume.Application.Common;

namespace UTperfume.Application.Features.Categories;

public sealed record CategorySummary(string Slug, string Name, string? ImageUrl, int ProductCount);

public sealed record CategoryDetail(string Slug, string Name, string? Description, string? ImageUrl, int ProductCount);

/// <summary>E7 and E8. Same rules as brands, ordered by display order then name (AC-K1).</summary>
public sealed class CategoryQueries(ICatalogDbContext db, IImageStorage storage)
{
    public async Task<IReadOnlyList<CategorySummary>> ListAsync(CancellationToken cancellationToken)
    {
        var counts = await db.Products.AsNoTracking().VisibleProducts()
            .SelectMany(p => p.Categories)
            .GroupBy(c => c.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count, cancellationToken);

        var categories = await db.Categories.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name).ThenBy(c => c.Id)
            .Select(c => new { c.Id, c.Slug, c.Name, c.ImageBlobPath })
            .ToListAsync(cancellationToken);

        return categories
            .Select(c => new CategorySummary(c.Slug, c.Name, UrlOrNull(c.ImageBlobPath), counts.GetValueOrDefault(c.Id)))
            .ToList();
    }

    /// <summary>BR-22: active categories with at least one visible product, by display order then name.</summary>
    public async Task<IReadOnlyList<CategorySummary>> ForHomeAsync(int max, CancellationToken cancellationToken)
    {
        var counts = await db.Products.AsNoTracking().VisibleProducts()
            .SelectMany(p => p.Categories)
            .GroupBy(c => c.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count, cancellationToken);

        var categories = await db.Categories.AsNoTracking()
            .Where(c => c.IsActive && counts.Keys.Contains(c.Id))
            .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name).ThenBy(c => c.Id)
            .Take(max)
            .Select(c => new { c.Id, c.Slug, c.Name, c.ImageBlobPath })
            .ToListAsync(cancellationToken);

        return categories.Select(c => new CategorySummary(c.Slug, c.Name, UrlOrNull(c.ImageBlobPath), counts[c.Id])).ToList();
    }

    public async Task<Result<CategoryDetail>> GetAsync(string slug, CancellationToken cancellationToken)
    {
        if (!Slug.IsValid(slug))
        {
            return Result<CategoryDetail>.NotFound();
        }

        var category = await db.Categories.AsNoTracking()
            .Where(c => c.IsActive && c.Slug == slug)
            .Select(c => new { c.Id, c.Slug, c.Name, c.Description, c.ImageBlobPath })
            .SingleOrDefaultAsync(cancellationToken);
        if (category is null)
        {
            return Result<CategoryDetail>.NotFound();
        }

        var count = await db.Products.AsNoTracking().VisibleProducts()
            .CountAsync(p => p.Categories.Any(c => c.CategoryId == category.Id), cancellationToken);
        return Result<CategoryDetail>.Ok(
            new CategoryDetail(category.Slug, category.Name, category.Description, UrlOrNull(category.ImageBlobPath), count));
    }

    private string? UrlOrNull(string? blobPath) => blobPath is null ? null : storage.GetPublicUrl(blobPath);
}
