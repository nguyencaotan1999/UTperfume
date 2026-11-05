using System.ComponentModel.DataAnnotations;

namespace UTperfume.Application.Common;

/// <summary>Catalogue behaviour settings bound from the <c>Catalog</c> configuration section.</summary>
public sealed class CatalogOptions
{
    public const string SectionName = "Catalog";

    /// <summary>Products published within this many days are flagged as new.</summary>
    [Range(1, 365)]
    public int NewBadgeDays { get; set; } = 30;

    /// <summary>A variant with a quantity at or below this value (and above zero) is reported as low stock.</summary>
    [Range(1, int.MaxValue)]
    public int LowStockThreshold { get; set; } = 5;
}
