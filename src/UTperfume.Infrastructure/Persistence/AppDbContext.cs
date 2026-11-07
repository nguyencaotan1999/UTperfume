using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using UTperfume.Application.Common;
using UTperfume.Application.Domain;
using UTperfume.Infrastructure.Identity;
using UTperfume.Infrastructure.Persistence.Configurations;

namespace UTperfume.Infrastructure.Persistence;

/// <summary>
/// The single DbContext and migration history (ADR-0012). Catalogue tables live in schema <c>catalog</c>.
/// The ASP.NET Core Identity tables live in schema <c>identity</c> (<c>AddAdminIdentity</c>, ADR-0010).
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AdminUser, IdentityRole<int>, int>(options), ICatalogDbContext
{
    public const string CatalogSchema = "catalog";

    public DbSet<Brand> Brands => Set<Brand>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Concentration> Concentrations => Set<Concentration>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();

    public DbSet<ProductImage> ProductImages => Set<ProductImage>();

    public DbSet<FragranceNote> FragranceNotes => Set<FragranceNote>();

    public DbSet<ProductNote> ProductNotes => Set<ProductNote>();

    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();

    public DbSet<HomeBanner> HomeBanners => Set<HomeBanner>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(CatalogSchema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        IdentityConfigurations.Apply(modelBuilder);
    }
}
