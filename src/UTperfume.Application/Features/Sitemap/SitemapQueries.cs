using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UTperfume.Application.Common;

namespace UTperfume.Application.Features.Sitemap;

/// <summary>Bound from <c>Sitemap</c>. The protocol allows at most 50,000 URLs per file, which is the default and the maximum.</summary>
public sealed class SitemapOptions
{
    public const string SectionName = "Sitemap";
    public const int ProtocolMaxUrls = 50_000;

    [Range(1, ProtocolMaxUrls)]
    public int MaxUrls { get; set; } = ProtocolMaxUrls;
}

/// <summary>A site-relative path, always starting with <c>/</c>, and an optional last-modified time.</summary>
public sealed record SitemapEntry(string Path, DateTime? LastModifiedUtc);

/// <summary>E10 data (AC-S3): home, catalogue, visible products, active brands and active categories only.</summary>
public sealed class SitemapQueries(ICatalogDbContext db, IOptions<SitemapOptions> options)
{
    public async Task<IReadOnlyList<SitemapEntry>> GetEntriesAsync(CancellationToken cancellationToken)
    {
        var max = options.Value.MaxUrls;
        var entries = new List<SitemapEntry> { new("/", null), new("/perfumes", null) };

        // Every query is bounded by what is still free under the cap, so a huge catalogue never loads in full.
        var brands = await db.Brands.AsNoTracking().Where(b => b.IsActive)
            .OrderBy(b => b.Slug).Take(Room(max, entries)).Select(b => new { b.Slug, b.UpdatedAtUtc }).ToListAsync(cancellationToken);
        entries.AddRange(brands.Select(b => new SitemapEntry($"/brands/{b.Slug}", b.UpdatedAtUtc)));

        var categories = await db.Categories.AsNoTracking().Where(c => c.IsActive)
            .OrderBy(c => c.Slug).Take(Room(max, entries)).Select(c => new { c.Slug, c.UpdatedAtUtc }).ToListAsync(cancellationToken);
        entries.AddRange(categories.Select(c => new SitemapEntry($"/categories/{c.Slug}", c.UpdatedAtUtc)));

        var products = await db.Products.AsNoTracking().VisibleProducts()
            .OrderBy(p => p.Slug)
            .Take(Room(max, entries))
            .Select(p => new { p.Slug, p.UpdatedAtUtc })
            .ToListAsync(cancellationToken);
        entries.AddRange(products.Select(p => new SitemapEntry($"/perfumes/{p.Slug}", p.UpdatedAtUtc)));

        return entries.Take(max).ToList();
    }

    private static int Room(int max, List<SitemapEntry> entries) => Math.Max(0, max - entries.Count);
}
