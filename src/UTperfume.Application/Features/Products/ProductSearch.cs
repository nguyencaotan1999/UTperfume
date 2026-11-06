using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UTperfume.Application.Common;
using UTperfume.Application.Domain;

namespace UTperfume.Application.Features.Products;

/// <summary>Raw E2 query values as bound by the API. Validation happens in <see cref="ProductSearchValidator"/>.</summary>
public sealed record ProductSearchRequest(
    string? Q,
    string[]? Brand,
    string[]? Category,
    string[]? Concentration,
    string[]? Volume,
    string? MinPrice,
    string? MaxPrice,
    string? Sort,
    string? Page,
    string? PageSize);

public enum ProductSort
{
    Newest,
    PriceAsc,
    PriceDesc,
    NameAsc,
    NameDesc,
}

/// <summary>A validated, normalised E2 query.</summary>
public sealed record ProductSearchCriteria(
    string? Q,
    string[] Brands,
    string[] Categories,
    string[] Concentrations,
    decimal[] Volumes,
    long? MinPrice,
    long? MaxPrice,
    ProductSort Sort,
    int Page,
    int PageSize);

public static partial class ProductSearchValidator
{
    public const int MaxValuesPerFilter = 20;
    public const int DefaultPageSize = 24;
    public const int MaxPageSize = 60;

    public static readonly IReadOnlyList<string> SortValues = ["newest", "price_asc", "price_desc", "name_asc", "name_desc"];

    /// <summary>Applies architecture section 6.6. Errors are keyed by the offending query parameter name.</summary>
    public static Result<ProductSearchCriteria> Validate(ProductSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        var q = request.Q?.Trim();
        if (string.IsNullOrEmpty(q))
        {
            q = null;
        }
        else if (q.Length is < 2 or > 100)
        {
            errors["q"] = ["q must be between 2 and 100 characters."];
        }

        var brands = Normalise(request.Brand);
        var categories = Normalise(request.Category);
        var concentrations = Normalise(request.Concentration);
        CheckSlugs("brand", brands, errors);
        CheckSlugs("category", categories, errors);
        CheckCount("concentration", concentrations.Length, errors);
        if (concentrations.Any(c => !ConcentrationCode().IsMatch(c)))
        {
            errors["concentration"] = ["concentration must be an upper-case code such as EDP."];
        }

        var volumes = ParseVolumes(request.Volume, errors);
        CheckCount("volume", volumes.Length, errors);

        var minPrice = ParsePrice("minPrice", request.MinPrice, errors);
        var maxPrice = ParsePrice("maxPrice", request.MaxPrice, errors);
        if (!errors.ContainsKey("minPrice") && !errors.ContainsKey("maxPrice")
            && minPrice is { } min && maxPrice is { } max && min > max)
        {
            errors["minPrice"] = ["minPrice must not be greater than maxPrice."];
        }

        var sort = ProductSort.Newest;
        if (request.Sort is not null)
        {
            switch (request.Sort)
            {
                case "newest": sort = ProductSort.Newest; break;
                case "price_asc": sort = ProductSort.PriceAsc; break;
                case "price_desc": sort = ProductSort.PriceDesc; break;
                case "name_asc": sort = ProductSort.NameAsc; break;
                case "name_desc": sort = ProductSort.NameDesc; break;
                default: errors["sort"] = [$"sort must be one of: {string.Join(", ", SortValues)}."]; break;
            }
        }

        var page = ParseInt("page", request.Page, errors) ?? 1;
        if (!errors.ContainsKey("page") && page < 1)
        {
            errors["page"] = ["page must be at least 1."];
        }

        var pageSize = ParseInt("pageSize", request.PageSize, errors) ?? DefaultPageSize;
        if (!errors.ContainsKey("pageSize") && pageSize is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = [$"pageSize must be between 1 and {MaxPageSize}."];
        }

        return errors.Count > 0
            ? Result<ProductSearchCriteria>.Invalid(errors)
            : Result<ProductSearchCriteria>.Ok(new ProductSearchCriteria(
                q, brands, categories, concentrations, volumes, minPrice, maxPrice, sort, page, pageSize));
    }

    /// <summary>
    /// Parses a whole-number query value in code, so error keys never depend on framework message text. A blank
    /// value counts as absent; anything else that is not an in-range integer is an error keyed by <paramref name="name"/>.
    /// </summary>
    public static int? ParseInt(string name, string? raw, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (int.TryParse(raw.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        errors[name] = [$"{name} must be a whole number."];
        return null;
    }

    private static long? ParsePrice(string name, string? raw, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!long.TryParse(raw.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            errors[name] = [$"{name} must be a whole number of dong."];
            return null;
        }

        if (value is < 0 or > Money.MaxFilterPrice)
        {
            errors[name] = [$"{name} must be a whole number of dong between 0 and {Money.MaxFilterPrice}."];
            return null;
        }

        return value;
    }

    private static decimal[] ParseVolumes(string[]? raw, Dictionary<string, string[]> errors)
    {
        var volumes = new List<decimal>();
        foreach (var item in (raw ?? []).Where(v => !string.IsNullOrWhiteSpace(v)))
        {
            // No exponent, so "1e400" is rejected rather than overflowing.
            if (!decimal.TryParse(item.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var value)
                || value <= 0 || value > VariantRules.MaxVolumeMl)
            {
                errors["volume"] = ["volume must be a number greater than 0 and at most 5000."];
                return [];
            }

            volumes.Add(value);
        }

        return volumes.Distinct().ToArray();
    }

    private static string[] Normalise(string[]? values) =>
        (values ?? []).Select(v => v.Trim()).Where(v => v.Length > 0).Distinct(StringComparer.Ordinal).ToArray();

    private static void CheckCount(string name, int count, Dictionary<string, string[]> errors)
    {
        if (count > MaxValuesPerFilter)
        {
            errors[name] = [$"{name} accepts at most {MaxValuesPerFilter} values."];
        }
    }

    private static void CheckSlugs(string name, string[] values, Dictionary<string, string[]> errors)
    {
        CheckCount(name, values.Length, errors);
        if (!errors.ContainsKey(name) && values.Any(v => !Slug.IsValid(v)))
        {
            errors[name] = [$"{name} must be a lower-case slug."];
        }
    }

    [GeneratedRegex(@"^[A-Z_]{2,20}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ConcentrationCode();
}

/// <summary>E2. Every filter applies to the visibility-filtered projection (BR-01, BR-11 to BR-16).</summary>
public sealed class ProductSearchHandler(
    ICatalogDbContext db,
    IImageStorage storage,
    IOptions<CatalogOptions> options,
    TimeProvider clock)
{
    public async Task<Result<PagedResult<ProductSummary>>> SearchAsync(
        ProductSearchRequest request, CancellationToken cancellationToken)
    {
        var validated = ProductSearchValidator.Validate(request);
        if (!validated.IsSuccess)
        {
            return Result<PagedResult<ProductSummary>>.Invalid(validated.Error!.Details!);
        }

        var c = validated.Value!;
        var products = db.Products.AsNoTracking().VisibleProducts();

        if (c.Brands.Length > 0)
        {
            var brands = c.Brands;
            products = products.Where(p => brands.Contains(p.Brand.Slug));
        }

        if (c.Categories.Length > 0)
        {
            var categories = c.Categories;
            products = products.Where(p => p.Categories.Any(pc => pc.Category.IsActive && categories.Contains(pc.Category.Slug)));
        }

        if (c.Concentrations.Length > 0)
        {
            var concentrations = c.Concentrations;
            products = products.Where(p => concentrations.Contains(p.Concentration.Code));
        }

        if (c.Volumes.Length > 0)
        {
            var volumes = c.Volumes;
            products = products.Where(p => p.Variants.Any(v => v.IsActive && volumes.Contains(v.VolumeMl)));
        }

        if (c.Q is { } q)
        {
            var pattern = $"%{EscapeLike(q)}%";
            var sku = q.ToUpperInvariant();
            products = products.Where(p =>
                EF.Functions.Like(p.Name, pattern, "\\")
                || EF.Functions.Like(p.Brand.Name, pattern, "\\")
                || p.Variants.Any(v => v.IsActive && v.Sku == sku));
        }

        // Price filters apply to the from-price, with the volume filter taken into account (BR-12), on top of the
        // visibility-filtered query; the projection below reads the same from-price.
        var volumeFilter = c.Volumes.Length > 0 ? c.Volumes : null;
        products = products.WherePriceBetween(c.MinPrice, c.MaxPrice, volumeFilter);

        var total = await products.CountAsync(cancellationToken);

        var skip = (long)(c.Page - 1) * c.PageSize;
        var items = new List<ProductSummary>();
        if (skip < total)
        {
            var ordered = c.Sort switch
            {
                ProductSort.PriceAsc => products.OrderByFromPrice(descending: false, volumeFilter),
                ProductSort.PriceDesc => products.OrderByFromPrice(descending: true, volumeFilter),
                ProductSort.NameAsc => products.OrderBy(p => p.Name).ThenBy(p => p.Id),
                ProductSort.NameDesc => products.OrderByDescending(p => p.Name).ThenBy(p => p.Id),
                _ => products.OrderByDescending(p => p.PublishedAtUtc).ThenBy(p => p.Id),
            };

            var page = await ordered.Skip((int)skip).Take(c.PageSize).ToSummaryRows(volumeFilter).ToListAsync(cancellationToken);
            var newSince = clock.GetUtcNow().UtcDateTime.AddDays(-options.Value.NewBadgeDays);
            items.AddRange(page.Select(r => ProductSummary.From(r, storage, newSince)));
        }

        return Result<PagedResult<ProductSummary>>.Ok(PagedResult<ProductSummary>.Create(items, c.Page, c.PageSize, total));
    }

    /// <summary>Escapes LIKE wildcards so that <c>%</c>, <c>_</c> and <c>[</c> match literally (BR-16).</summary>
    internal static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal);
}
