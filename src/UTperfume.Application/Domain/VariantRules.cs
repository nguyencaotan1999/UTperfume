using System.Text.RegularExpressions;

namespace UTperfume.Application.Domain;

/// <summary>Variant validation shared by the admin API and the seeder (BR-05, BR-06).</summary>
public static partial class VariantRules
{
    public const int MaxSkuLength = 64;

    public const decimal MaxVolumeMl = 5000m;

    /// <summary>Trimmed and upper-cased, as stored.</summary>
    public static string NormalizeSku(string sku)
    {
        ArgumentNullException.ThrowIfNull(sku);
        return sku.Trim().ToUpperInvariant();
    }

    /// <summary>True for an already-normalised SKU that matches <c>^[A-Z0-9][A-Z0-9-_.]{0,63}$</c>.</summary>
    public static bool IsValidSku(string? sku) => sku is not null && SkuPattern().IsMatch(sku);

    /// <summary>Greater than 0, at most 5000, one decimal place.</summary>
    public static bool IsValidVolume(decimal volumeMl) =>
        volumeMl > 0 && volumeMl <= MaxVolumeMl && decimal.Round(volumeMl, 1) == volumeMl;

    [GeneratedRegex(@"^[A-Z0-9][A-Z0-9\-_.]{0,63}\z", RegexOptions.CultureInvariant)]
    private static partial Regex SkuPattern();
}
