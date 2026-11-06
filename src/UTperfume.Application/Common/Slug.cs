using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace UTperfume.Application.Common;

/// <summary>Slug rules (BR-25, ADR-0006): lowercase ASCII, hyphen separated, 1 to 200 characters.</summary>
public static partial class Slug
{
    public const int MaxLength = 200;

    public static bool IsValid(string? slug) =>
        !string.IsNullOrEmpty(slug) && slug.Length <= MaxLength && Pattern().IsMatch(slug);

    /// <summary>
    /// Builds a slug from a display name. Diacritics are removed, <c>đ</c> becomes <c>d</c>, and every other
    /// run of non-alphanumeric characters becomes one hyphen. Returns an empty string when the name has no
    /// usable characters; the caller must reject that.
    /// </summary>
    public static string Generate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(name.Length);
        var pendingSeparator = false;

        foreach (var ch in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var mapped = Transliterate(ch);
            if (mapped is null)
            {
                pendingSeparator = builder.Length > 0;
                continue;
            }

            if (pendingSeparator)
            {
                builder.Append('-');
                pendingSeparator = false;
            }

            builder.Append(mapped);
        }

        var slug = builder.ToString();
        if (slug.Length > MaxLength)
        {
            slug = slug[..MaxLength].TrimEnd('-');
        }

        return slug;
    }

    private static string? Transliterate(char ch) => ch switch
    {
        >= 'a' and <= 'z' => ch.ToString(),
        >= '0' and <= '9' => ch.ToString(),
        >= 'A' and <= 'Z' => char.ToLowerInvariant(ch).ToString(),
        'đ' or 'Đ' => "d",
        'ß' => "ss",
        'æ' or 'Æ' => "ae",
        'œ' or 'Œ' => "oe",
        'ø' or 'Ø' => "o",
        'ł' or 'Ł' => "l",
        _ => null,
    };

    [GeneratedRegex(@"^[a-z0-9]+(?:-[a-z0-9]+)*\z",RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
