using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UTperfume.Application.Domain;

namespace UTperfume.Infrastructure.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Product", t =>
        {
            t.HasCheckConstraint("CK_Product_SlugFormat", ConfigurationHelpers.SlugFormatSql);
            t.HasCheckConstraint("CK_Product_StatusValid", "[Status] IN (0,1,2)");
            t.HasCheckConstraint("CK_Product_PublishedAtRequired", "[Status] <> 1 OR [PublishedAtUtc] IS NOT NULL");
        });
        builder.HasKey(p => p.Id).HasName("PK_Product");
        builder.Property(p => p.Name).HasColumnType("nvarchar(200)").IsRequired();
        builder.Property(p => p.Slug).HasColumnType("varchar(200)").IsRequired();
        builder.Property(p => p.ShortDescription).HasColumnType("nvarchar(500)");
        builder.Property(p => p.Description).HasColumnType("nvarchar(max)");
        builder.Property(p => p.Status).HasColumnType("tinyint").HasConversion<byte>().WithDefault(ProductStatus.Draft);
        builder.Property(p => p.PublishedAtUtc).HasColumnType(ConfigurationHelpers.Utc);
        builder.Property(p => p.IsFeatured).WithDefault(false);
        builder.Property(p => p.IsBestSeller).WithDefault(false);
        builder.Property(p => p.MetaTitle).HasColumnType("nvarchar(200)");
        builder.Property(p => p.MetaDescription).HasColumnType("nvarchar(320)");
        builder.ConfigureAudit();

        builder.HasOne(p => p.Brand).WithMany(b => b.Products).HasForeignKey(p => p.BrandId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Product_Brand");
        builder.HasOne(p => p.Concentration).WithMany().HasForeignKey(p => p.ConcentrationId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Product_Concentration");

        builder.HasIndex(p => p.Slug).IsUnique().HasDatabaseName("UX_Product_Slug");
        builder.HasIndex(p => p.BrandId).HasDatabaseName("IX_Product_BrandId");
        builder.HasIndex(p => p.ConcentrationId).HasDatabaseName("IX_Product_ConcentrationId");
        builder.HasIndex(p => new { p.Status, p.PublishedAtUtc, p.Id })
            .IsDescending(false, true, false)
            .IncludeProperties(p => new { p.BrandId, p.ConcentrationId, p.Name, p.Slug, p.IsFeatured, p.IsBestSeller })
            .HasDatabaseName("IX_Product_Status_PublishedAtUtc");
        builder.HasIndex(p => new { p.Status, p.Name, p.Id }).HasDatabaseName("IX_Product_Status_Name");
    }
}

internal sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.ToTable("ProductVariant", t =>
        {
            t.HasCheckConstraint("CK_ProductVariant_VolumeRange", "[VolumeMl] > 0 AND [VolumeMl] <= 5000");
            t.HasCheckConstraint("CK_ProductVariant_PriceNonNegative", "[Price] >= 0");
            t.HasCheckConstraint("CK_ProductVariant_OriginalPriceGreater", "[OriginalPrice] IS NULL OR [OriginalPrice] > [Price]");
            t.HasCheckConstraint("CK_ProductVariant_StockNonNegative", "[StockQuantity] >= 0");
        });
        builder.HasKey(v => v.Id).HasName("PK_ProductVariant");
        builder.Property(v => v.Sku).HasColumnType("varchar(64)").IsRequired();
        builder.Property(v => v.VolumeMl).HasColumnType("decimal(6,1)");
        builder.Property(v => v.Price).HasColumnType("bigint");
        builder.Property(v => v.OriginalPrice).HasColumnType("bigint");
        builder.Property(v => v.StockQuantity).WithDefault(0);
        builder.Property(v => v.IsActive).WithDefault(true);
        builder.ConfigureAudit();

        // NO ACTION: variants are deactivated, never deleted once referenced by future orders.
        builder.HasOne(v => v.Product).WithMany(p => p.Variants).HasForeignKey(v => v.ProductId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_ProductVariant_Product");

        builder.HasIndex(v => v.Sku).IsUnique().HasDatabaseName("UX_ProductVariant_Sku");
        builder.HasIndex(v => new { v.ProductId, v.VolumeMl }).IsUnique().HasDatabaseName("UX_ProductVariant_ProductId_VolumeMl");
        builder.HasIndex(v => new { v.ProductId, v.IsActive })
            .IncludeProperties(v => new { v.Price, v.OriginalPrice, v.VolumeMl, v.StockQuantity })
            .HasDatabaseName("IX_ProductVariant_ProductId_IsActive");
        builder.HasIndex(v => v.VolumeMl)
            .IncludeProperties(v => new { v.ProductId, v.IsActive, v.Price })
            .HasDatabaseName("IX_ProductVariant_VolumeMl");
    }
}

internal sealed class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> builder)
    {
        builder.ToTable("ProductImage", t =>
        {
            t.HasCheckConstraint("CK_ProductImage_ContentType",
                "[ContentType] IN ('image/jpeg','image/png','image/webp')");
            t.HasCheckConstraint("CK_ProductImage_SizeBytes", "[SizeBytes] > 0 AND [SizeBytes] <= 5242880");
        });
        builder.HasKey(i => i.Id).HasName("PK_ProductImage");
        builder.Property(i => i.BlobPath).HasColumnType("varchar(300)").IsRequired();
        builder.Property(i => i.ContentType).HasColumnType("varchar(50)").IsRequired();
        builder.Property(i => i.OriginalFileName).HasColumnType("nvarchar(255)");
        builder.Property(i => i.AltText).HasColumnType("nvarchar(250)");
        builder.Property(i => i.SortOrder).WithDefault(0);
        builder.Property(i => i.IsPrimary).WithDefault(false);
        builder.Property(i => i.CreatedAtUtc).HasColumnType(ConfigurationHelpers.Utc);
        builder.Property(i => i.CreatedBy).HasColumnType("nvarchar(100)");

        builder.HasOne(i => i.Product).WithMany(p => p.Images).HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_ProductImage_Product");

        builder.HasIndex(i => i.BlobPath).IsUnique().HasDatabaseName("UX_ProductImage_BlobPath");
        builder.HasIndex(i => new { i.ProductId, i.SortOrder }).HasDatabaseName("IX_ProductImage_ProductId_SortOrder");
        builder.HasIndex(i => i.ProductId).IsUnique().HasFilter("[IsPrimary] = 1")
            .HasDatabaseName("UX_ProductImage_ProductId_Primary");
    }
}

internal sealed class ProductNoteConfiguration : IEntityTypeConfiguration<ProductNote>
{
    public void Configure(EntityTypeBuilder<ProductNote> builder)
    {
        builder.ToTable("ProductNote", t => t.HasCheckConstraint("CK_ProductNote_NoteTypeValid", "[NoteType] IN (1,2,3)"));
        builder.HasKey(n => new { n.ProductId, n.NoteType, n.FragranceNoteId }).HasName("PK_ProductNote");
        builder.Property(n => n.NoteType).HasColumnType("tinyint").HasConversion<byte>();
        builder.Property(n => n.SortOrder).HasColumnType("smallint").WithDefault((short)0);

        builder.HasOne(n => n.Product).WithMany(p => p.Notes).HasForeignKey(n => n.ProductId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_ProductNote_Product");
        builder.HasOne(n => n.FragranceNote).WithMany().HasForeignKey(n => n.FragranceNoteId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_ProductNote_FragranceNote");

        builder.HasIndex(n => n.FragranceNoteId).HasDatabaseName("IX_ProductNote_FragranceNoteId");
    }
}

internal sealed class ProductCategoryConfiguration : IEntityTypeConfiguration<ProductCategory>
{
    public void Configure(EntityTypeBuilder<ProductCategory> builder)
    {
        builder.ToTable("ProductCategory");
        builder.HasKey(c => new { c.ProductId, c.CategoryId }).HasName("PK_ProductCategory");

        builder.HasOne(c => c.Product).WithMany(p => p.Categories).HasForeignKey(c => c.ProductId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_ProductCategory_Product");
        builder.HasOne(c => c.Category).WithMany(c => c.ProductCategories).HasForeignKey(c => c.CategoryId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_ProductCategory_Category");

        builder.HasIndex(c => new { c.CategoryId, c.ProductId }).HasDatabaseName("IX_ProductCategory_CategoryId_ProductId");
    }
}
