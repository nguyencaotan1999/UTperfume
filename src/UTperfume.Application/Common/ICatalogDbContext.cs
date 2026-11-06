using Microsoft.EntityFrameworkCore;
using UTperfume.Application.Domain;

namespace UTperfume.Application.Common;

/// <summary>Data access abstraction for handlers (ADR-0012). There is no repository layer.</summary>
public interface ICatalogDbContext
{
    DbSet<Brand> Brands { get; }

    DbSet<Category> Categories { get; }

    DbSet<Concentration> Concentrations { get; }

    DbSet<Product> Products { get; }

    DbSet<ProductVariant> ProductVariants { get; }

    DbSet<ProductImage> ProductImages { get; }

    DbSet<FragranceNote> FragranceNotes { get; }

    DbSet<ProductNote> ProductNotes { get; }

    DbSet<ProductCategory> ProductCategories { get; }

    DbSet<HomeBanner> HomeBanners { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
