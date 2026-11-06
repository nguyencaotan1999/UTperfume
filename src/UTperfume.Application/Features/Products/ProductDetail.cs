using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UTperfume.Application.Common;
using UTperfume.Application.Domain;

namespace UTperfume.Application.Features.Products;

public sealed record NoteRef(string Slug, string Name);

public sealed record ProductNotes(IReadOnlyList<NoteRef> Top, IReadOnlyList<NoteRef> Heart, IReadOnlyList<NoteRef> Base);

public sealed record ProductImageDto(string Url, string? AltText, bool IsPrimary, int? Width, int? Height);

/// <summary>Public variant. It carries the availability status only, never a quantity (BR-08, AC-P2b).</summary>
public sealed record VariantDto(
    string Sku,
    decimal VolumeMl,
    long Price,
    long? OriginalPrice,
    int? DiscountPercent,
    AvailabilityStatus Availability);

public sealed record SeoDto(string MetaTitle, string? MetaDescription);

public sealed record ProductDetail(
    int Id,
    string Slug,
    string Name,
    BrandRef Brand,
    ConcentrationRef Concentration,
    string? ShortDescription,
    string? Description,
    IReadOnlyList<CategoryRef> Categories,
    IReadOnlyList<ProductImageDto> Images,
    ProductNotes Notes,
    IReadOnlyList<VariantDto> Variants,
    SeoDto Seo,
    DateTime? PublishedAtUtc)
{
    public string Currency => Money.Currency;
}

/// <summary>E3 and E4. A product that is not visible (BR-01) is "not found" and the reason is never disclosed.</summary>
public sealed class ProductDetailHandler(
    ICatalogDbContext db,
    IImageStorage storage,
    IOptions<CatalogOptions> options,
    TimeProvider clock)
{
    public const int DefaultRelatedLimit = 8;
    public const int MaxRelatedLimit = 12;
    private const string SiteName = "UTperfume";

    public async Task<Result<ProductDetail>> GetAsync(string slug, CancellationToken cancellationToken)
    {
        if (!Slug.IsValid(slug))
        {
            return Result<ProductDetail>.NotFound();
        }

        var product = await db.Products.AsNoTracking().VisibleProducts()
            .Where(p => p.Slug == slug)
            .Select(p => new
            {
                p.Id,
                p.Slug,
                p.Name,
                BrandSlug = p.Brand.Slug,
                BrandName = p.Brand.Name,
                ConcentrationCode = p.Concentration.Code,
                ConcentrationName = p.Concentration.Name,
                p.ShortDescription,
                p.Description,
                p.MetaTitle,
                p.MetaDescription,
                p.PublishedAtUtc,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (product is null)
        {
            return Result<ProductDetail>.NotFound();
        }

        var categories = await db.ProductCategories.AsNoTracking()
            .Where(pc => pc.ProductId == product.Id && pc.Category.IsActive)
            .OrderBy(pc => pc.Category.DisplayOrder).ThenBy(pc => pc.Category.Name)
            .Select(pc => new CategoryRef(pc.Category.Slug, pc.Category.Name))
            .ToListAsync(cancellationToken);

        // Primary first, then by sort order (AC-P1).
        var images = await db.ProductImages.AsNoTracking()
            .Where(i => i.ProductId == product.Id)
            .OrderByDescending(i => i.IsPrimary).ThenBy(i => i.SortOrder).ThenBy(i => i.Id)
            .Select(i => new { i.BlobPath, i.AltText, i.IsPrimary, i.Width, i.Height })
            .ToListAsync(cancellationToken);

        var notes = await db.ProductNotes.AsNoTracking()
            .Where(n => n.ProductId == product.Id)
            .OrderBy(n => n.SortOrder).ThenBy(n => n.FragranceNote.Name)
            .Select(n => new { n.NoteType, n.FragranceNote.Slug, n.FragranceNote.Name })
            .ToListAsync(cancellationToken);

        // Active variants only (AC-P3); availability is computed in SQL and the quantity is never selected.
        var variants = await db.ProductVariants.AsNoTracking()
            .Where(v => v.ProductId == product.Id)
            .OrderBy(v => v.VolumeMl)
            .ToAvailabilityRows(options.Value.LowStockThreshold)
            .ToListAsync(cancellationToken);

        NoteRef[] Tier(NoteType tier) =>
            notes.Where(n => n.NoteType == tier).Select(n => new NoteRef(n.Slug, n.Name)).ToArray();

        var fallbackDescription = string.IsNullOrWhiteSpace(product.ShortDescription) ? null : product.ShortDescription;
        return Result<ProductDetail>.Ok(new ProductDetail(
            product.Id,
            product.Slug,
            product.Name,
            new BrandRef(product.BrandSlug, product.BrandName),
            new ConcentrationRef(product.ConcentrationCode, product.ConcentrationName),
            product.ShortDescription,
            product.Description,
            categories,
            images.Select(i => new ProductImageDto(
                storage.GetPublicUrl(i.BlobPath), i.AltText ?? product.Name, i.IsPrimary, i.Width, i.Height)).ToList(),
            new ProductNotes(Tier(NoteType.Top), Tier(NoteType.Heart), Tier(NoteType.Base)),
            variants.Select(v => new VariantDto(
                v.Sku, v.VolumeMl, v.Price, v.OriginalPrice, Money.DiscountPercent(v.Price, v.OriginalPrice), v.Availability)).ToList(),
            new SeoDto(
                product.MetaTitle ?? $"{product.Name} – {product.BrandName} | {SiteName}",
                product.MetaDescription ?? fallbackDescription),
            product.PublishedAtUtc));
    }

    /// <summary>
    /// BR-24: visible products other than this one that share a category or the brand. Ranked by shared categories
    /// (descending), same brand first, newest, then id.
    /// </summary>
    public async Task<Result<IReadOnlyList<ProductSummary>>> RelatedAsync(
        string slug, string? limit, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var take = ProductSearchValidator.ParseInt("limit", limit, errors) ?? DefaultRelatedLimit;
        if (!errors.ContainsKey("limit") && take is < 1 or > MaxRelatedLimit)
        {
            errors["limit"] = [$"limit must be between 1 and {MaxRelatedLimit}."];
        }

        if (errors.Count > 0)
        {
            return Result<IReadOnlyList<ProductSummary>>.Invalid(errors);
        }

        if (!Slug.IsValid(slug))
        {
            return Result<IReadOnlyList<ProductSummary>>.NotFound();
        }

        var current = await db.Products.AsNoTracking().VisibleProducts()
            .Where(p => p.Slug == slug)
            .Select(p => new { p.Id, p.BrandId })
            .SingleOrDefaultAsync(cancellationToken);
        if (current is null)
        {
            return Result<IReadOnlyList<ProductSummary>>.NotFound();
        }

        var categoryIds = await db.ProductCategories.AsNoTracking()
            .Where(pc => pc.ProductId == current.Id && pc.Category.IsActive)
            .Select(pc => pc.CategoryId)
            .ToListAsync(cancellationToken);
        var brandId = current.BrandId;
        var currentId = current.Id;

        var rankedIds = await db.Products.AsNoTracking().VisibleProducts()
            .Where(p => p.Id != currentId
                        && (p.BrandId == brandId || p.Categories.Any(pc => categoryIds.Contains(pc.CategoryId))))
            .Select(p => new
            {
                p.Id,
                p.PublishedAtUtc,
                Shared = p.Categories.Count(pc => categoryIds.Contains(pc.CategoryId)),
                SameBrand = p.BrandId == brandId,
            })
            .OrderByDescending(x => x.Shared)
            .ThenByDescending(x => x.SameBrand)
            .ThenByDescending(x => x.PublishedAtUtc)
            .ThenBy(x => x.Id)
            .Take(take)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        if (rankedIds.Count == 0)
        {
            return Result<IReadOnlyList<ProductSummary>>.Ok([]);
        }

        var rows = await db.Products.AsNoTracking().VisibleProducts()
            .Where(p => rankedIds.Contains(p.Id))
            .ToSummaryRows()
            .ToListAsync(cancellationToken);

        var newSince = clock.GetUtcNow().UtcDateTime.AddDays(-options.Value.NewBadgeDays);
        var byId = rows.ToDictionary(r => r.Id);
        IReadOnlyList<ProductSummary> ordered = rankedIds
            .Where(byId.ContainsKey)
            .Select(id => ProductSummary.From(byId[id], storage, newSince))
            .ToList();
        return Result<IReadOnlyList<ProductSummary>>.Ok(ordered);
    }
}
