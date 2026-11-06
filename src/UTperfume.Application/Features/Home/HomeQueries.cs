using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UTperfume.Application.Common;
using UTperfume.Application.Features.Brands;
using UTperfume.Application.Features.Categories;
using UTperfume.Application.Features.Products;

namespace UTperfume.Application.Features.Home;

public sealed record BannerDto(string Title, string? Subtitle, string ImageUrl, string AltText, string? LinkUrl);

/// <summary>E1. Every section is always present; with no data each is an empty array (AC-H4).</summary>
public sealed record HomeResponse(
    IReadOnlyList<BannerDto> Banners,
    IReadOnlyList<ProductSummary> FeaturedProducts,
    IReadOnlyList<ProductSummary> NewArrivals,
    IReadOnlyList<ProductSummary> BestSellers,
    IReadOnlyList<BrandSummary> PopularBrands,
    IReadOnlyList<CategorySummary> Categories);

public sealed class HomeQueries(
    ICatalogDbContext db,
    IImageStorage storage,
    IOptions<CatalogOptions> options,
    TimeProvider clock,
    BrandQueries brands,
    CategoryQueries categories)
{
    public const int MaxBanners = 5;
    public const int MaxProductsPerSection = 8;
    public const int MaxBrands = 12;
    public const int MaxCategories = 12;

    public async Task<HomeResponse> GetAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var newSince = now.AddDays(-options.Value.NewBadgeDays);

        // BR-23: [StartsAtUtc, EndsAtUtc), both optional.
        var banners = await db.HomeBanners.AsNoTracking()
            .Where(b => b.IsActive
                        && (b.StartsAtUtc == null || b.StartsAtUtc <= now)
                        && (b.EndsAtUtc == null || b.EndsAtUtc > now))
            .OrderBy(b => b.SortOrder).ThenBy(b => b.Id)
            .Take(MaxBanners)
            .Select(b => new { b.Title, b.Subtitle, b.ImageBlobPath, b.AltText, b.LinkUrl })
            .ToListAsync(cancellationToken);

        // Every product section starts from the shared visibility rule (BR-01); a hidden product is in no section.
        var visible = db.Products.AsNoTracking().VisibleProducts();
        var featured = await SectionAsync(visible.Where(p => p.IsFeatured), newSince, cancellationToken);   // BR-18
        var newest = await SectionAsync(visible, newSince, cancellationToken);                               // BR-19
        var bestSellers = await SectionAsync(visible.Where(p => p.IsBestSeller), newSince, cancellationToken); // BR-20

        return new HomeResponse(
            banners.Select(b => new BannerDto(b.Title, b.Subtitle, storage.GetPublicUrl(b.ImageBlobPath), b.AltText, b.LinkUrl)).ToList(),
            featured,
            newest,
            bestSellers,
            await brands.PopularAsync(MaxBrands, cancellationToken),
            await categories.ForHomeAsync(MaxCategories, cancellationToken));
    }

    private async Task<IReadOnlyList<ProductSummary>> SectionAsync(
        IQueryable<Domain.Product> products, DateTime newSince, CancellationToken cancellationToken)
    {
        var rows = await products
            .OrderByDescending(p => p.PublishedAtUtc).ThenBy(p => p.Id)
            .Take(MaxProductsPerSection)
            .ToSummaryRows()
            .ToListAsync(cancellationToken);
        return rows.Select(r => ProductSummary.From(r, storage, newSince)).ToList();
    }
}
