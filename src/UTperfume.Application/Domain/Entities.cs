namespace UTperfume.Application.Domain;

/// <summary>Audit and concurrency columns shared by admin-editable tables (0002 section 1).</summary>
public abstract class AuditableEntity
{
    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Identity user id as text. No foreign key, so audit values survive user removal.</summary>
    public string? UpdatedBy { get; set; }

    public byte[] RowVersion { get; set; } = [];
}

public class Brand : AuditableEntity
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Blob path under <c>brands/</c>.</summary>
    public string? LogoBlobPath { get; set; }

    public bool IsFeatured { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public List<Product> Products { get; set; } = [];
}

public class Category : AuditableEntity
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Blob path under <c>categories/</c>.</summary>
    public string? ImageBlobPath { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public List<ProductCategory> ProductCategories { get; set; } = [];
}

/// <summary>Seeded lookup (ADR-0002). Ids are stable and not generated.</summary>
public class Concentration
{
    public short Id { get; set; }

    /// <summary>Upper-case code such as <c>EDP</c> or <c>EAU_FRAICHE</c>.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public short SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}

public class Product : AuditableEntity
{
    public int Id { get; set; }

    public int BrandId { get; set; }

    public Brand Brand { get; set; } = null!;

    public short ConcentrationId { get; set; }

    public Concentration Concentration { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string? ShortDescription { get; set; }

    /// <summary>Plain text. The application enforces at most 10,000 characters.</summary>
    public string? Description { get; set; }

    public ProductStatus Status { get; set; } = ProductStatus.Draft;

    /// <summary>Set on first publish. Required when <see cref="Status"/> is Published.</summary>
    public DateTime? PublishedAtUtc { get; set; }

    public bool IsFeatured { get; set; }

    public bool IsBestSeller { get; set; }

    public string? MetaTitle { get; set; }

    public string? MetaDescription { get; set; }

    public List<ProductVariant> Variants { get; set; } = [];

    public List<ProductImage> Images { get; set; } = [];

    public List<ProductNote> Notes { get; set; } = [];

    public List<ProductCategory> Categories { get; set; } = [];
}

public class ProductVariant : AuditableEntity
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public Product Product { get; set; } = null!;

    /// <summary>Stored upper-case, globally unique. Use <see cref="VariantRules.NormalizeSku"/>.</summary>
    public string Sku { get; set; } = string.Empty;

    public decimal VolumeMl { get; set; }

    /// <summary>Whole VND.</summary>
    public long Price { get; set; }

    /// <summary>Whole VND compare-at price. Must be greater than <see cref="Price"/> when set.</summary>
    public long? OriginalPrice { get; set; }

    /// <summary>Admin-only. Never copied into a public DTO or projection (BR-05, BR-28).</summary>
    public int StockQuantity { get; set; }

    public bool IsActive { get; set; } = true;
}

public class ProductImage
{
    public const int MaxSizeBytes = 5 * 1024 * 1024;

    public static readonly IReadOnlyList<string> AllowedContentTypes = ["image/jpeg", "image/png", "image/webp"];

    public int Id { get; set; }

    public int ProductId { get; set; }

    public Product Product { get; set; } = null!;

    /// <summary>Relative to the image container, server-generated: <c>products/{productId}/{guid}.{ext}</c>.</summary>
    public string BlobPath { get; set; } = string.Empty;

    /// <summary>One of <see cref="AllowedContentTypes"/>.</summary>
    public string ContentType { get; set; } = string.Empty;

    public int SizeBytes { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    /// <summary>Display only. Never used in a path or header.</summary>
    public string? OriginalFileName { get; set; }

    public string? AltText { get; set; }

    public int SortOrder { get; set; }

    public bool IsPrimary { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedBy { get; set; }
}

public class FragranceNote
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Key is (ProductId, NoteType, FragranceNoteId): a note may appear once per tier (ADR-0003).</summary>
public class ProductNote
{
    public int ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public NoteType NoteType { get; set; }

    public int FragranceNoteId { get; set; }

    public FragranceNote FragranceNote { get; set; } = null!;

    public short SortOrder { get; set; }
}

public class ProductCategory
{
    public int ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public int CategoryId { get; set; }

    public Category Category { get; set; } = null!;
}

public class HomeBanner : AuditableEntity
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Subtitle { get; set; }

    /// <summary>Blob path under <c>banners/</c>.</summary>
    public string ImageBlobPath { get; set; } = string.Empty;

    public string AltText { get; set; } = string.Empty;

    /// <summary>Site-relative path starting with a single slash (BR-23).</summary>
    public string? LinkUrl { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime? StartsAtUtc { get; set; }

    public DateTime? EndsAtUtc { get; set; }
}
