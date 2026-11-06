using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UTperfume.Application.Domain;

namespace UTperfume.Infrastructure.Persistence.Configurations;

internal sealed class BrandConfiguration : IEntityTypeConfiguration<Brand>
{
    public void Configure(EntityTypeBuilder<Brand> builder)
    {
        builder.ToTable("Brand", t => t.HasCheckConstraint("CK_Brand_SlugFormat", ConfigurationHelpers.SlugFormatSql));
        builder.HasKey(b => b.Id).HasName("PK_Brand");
        builder.Property(b => b.Name).HasColumnType("nvarchar(150)").IsRequired();
        builder.Property(b => b.Slug).HasColumnType("varchar(200)").IsRequired();
        builder.Property(b => b.Description).HasColumnType("nvarchar(2000)");
        builder.Property(b => b.LogoBlobPath).HasColumnType("varchar(300)");
        builder.Property(b => b.IsFeatured).WithDefault(false);
        builder.Property(b => b.DisplayOrder).WithDefault(0);
        builder.Property(b => b.IsActive).WithDefault(true);
        builder.ConfigureAudit();

        builder.HasIndex(b => b.Slug).IsUnique().HasDatabaseName("UX_Brand_Slug");
        builder.HasIndex(b => b.Name).IsUnique().HasDatabaseName("UX_Brand_Name");
    }
}

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Category", t => t.HasCheckConstraint("CK_Category_SlugFormat", ConfigurationHelpers.SlugFormatSql));
        builder.HasKey(c => c.Id).HasName("PK_Category");
        builder.Property(c => c.Name).HasColumnType("nvarchar(100)").IsRequired();
        builder.Property(c => c.Slug).HasColumnType("varchar(200)").IsRequired();
        builder.Property(c => c.Description).HasColumnType("nvarchar(2000)");
        builder.Property(c => c.ImageBlobPath).HasColumnType("varchar(300)");
        builder.Property(c => c.DisplayOrder).WithDefault(0);
        builder.Property(c => c.IsActive).WithDefault(true);
        builder.ConfigureAudit();

        builder.HasIndex(c => c.Slug).IsUnique().HasDatabaseName("UX_Category_Slug");
        builder.HasIndex(c => c.Name).IsUnique().HasDatabaseName("UX_Category_Name");
    }
}

internal sealed class ConcentrationConfiguration : IEntityTypeConfiguration<Concentration>
{
    public void Configure(EntityTypeBuilder<Concentration> builder)
    {
        builder.ToTable("Concentration", t => t.HasCheckConstraint("CK_Concentration_CodeFormat", "[Code] NOT LIKE '%[^A-Z_]%' COLLATE Latin1_General_BIN2"));
        builder.HasKey(c => c.Id).HasName("PK_Concentration");
        builder.Property(c => c.Id).HasColumnType("smallint").ValueGeneratedNever();
        builder.Property(c => c.Code).HasColumnType("varchar(20)").IsRequired();
        builder.Property(c => c.Name).HasColumnType("nvarchar(50)").IsRequired();
        builder.Property(c => c.SortOrder).HasColumnType("smallint");
        builder.Property(c => c.IsActive).WithDefault(true);

        builder.HasIndex(c => c.Code).IsUnique().HasDatabaseName("UX_Concentration_Code");

        // The list needs business confirmation (0002 section 3.3). Changes here produce UPDATE/DELETE in migrations.
        builder.HasData(
            new Concentration { Id = 1, Code = "PARFUM", Name = "Parfum", SortOrder = 1, IsActive = true },
            new Concentration { Id = 2, Code = "EXTRAIT", Name = "Extrait de Parfum", SortOrder = 2, IsActive = true },
            new Concentration { Id = 3, Code = "EDP", Name = "Eau de Parfum", SortOrder = 3, IsActive = true },
            new Concentration { Id = 4, Code = "EDT", Name = "Eau de Toilette", SortOrder = 4, IsActive = true },
            new Concentration { Id = 5, Code = "EDC", Name = "Eau de Cologne", SortOrder = 5, IsActive = true },
            new Concentration { Id = 6, Code = "EAU_FRAICHE", Name = "Eau Fraîche", SortOrder = 6, IsActive = true });
    }
}

internal sealed class FragranceNoteConfiguration : IEntityTypeConfiguration<FragranceNote>
{
    public void Configure(EntityTypeBuilder<FragranceNote> builder)
    {
        builder.ToTable("FragranceNote", t => t.HasCheckConstraint("CK_FragranceNote_SlugFormat", ConfigurationHelpers.SlugFormatSql));
        builder.HasKey(n => n.Id).HasName("PK_FragranceNote");
        builder.Property(n => n.Name).HasColumnType("nvarchar(100)").IsRequired();
        builder.Property(n => n.Slug).HasColumnType("varchar(200)").IsRequired();
        builder.Property(n => n.CreatedAtUtc).HasColumnType(ConfigurationHelpers.Utc);

        builder.HasIndex(n => n.Name).IsUnique().HasDatabaseName("UX_FragranceNote_Name");
        builder.HasIndex(n => n.Slug).IsUnique().HasDatabaseName("UX_FragranceNote_Slug");
    }
}

internal sealed class HomeBannerConfiguration : IEntityTypeConfiguration<HomeBanner>
{
    public void Configure(EntityTypeBuilder<HomeBanner> builder)
    {
        builder.ToTable("HomeBanner", t =>
        {
            t.HasCheckConstraint("CK_HomeBanner_LinkUrlRelative",
                "[LinkUrl] IS NULL OR ([LinkUrl] LIKE '/%' AND [LinkUrl] NOT LIKE '//%')");
            t.HasCheckConstraint("CK_HomeBanner_Window",
                "[StartsAtUtc] IS NULL OR [EndsAtUtc] IS NULL OR [EndsAtUtc] > [StartsAtUtc]");
        });
        builder.HasKey(b => b.Id).HasName("PK_HomeBanner");
        builder.Property(b => b.Title).HasColumnType("nvarchar(150)").IsRequired();
        builder.Property(b => b.Subtitle).HasColumnType("nvarchar(300)");
        builder.Property(b => b.ImageBlobPath).HasColumnType("varchar(300)").IsRequired();
        builder.Property(b => b.AltText).HasColumnType("nvarchar(250)").IsRequired();
        builder.Property(b => b.LinkUrl).HasColumnType("nvarchar(500)");
        builder.Property(b => b.SortOrder).WithDefault(0);
        builder.Property(b => b.IsActive).WithDefault(true);
        builder.Property(b => b.StartsAtUtc).HasColumnType(ConfigurationHelpers.Utc);
        builder.Property(b => b.EndsAtUtc).HasColumnType(ConfigurationHelpers.Utc);
        builder.ConfigureAudit();

        builder.HasIndex(b => new { b.IsActive, b.SortOrder }).HasDatabaseName("IX_HomeBanner_IsActive_SortOrder");
    }
}
