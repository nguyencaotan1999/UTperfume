using UTperfume.Application.Common;

namespace UTperfume.Application.Features.Products;

/// <summary>
/// Public product card (E2 and E4). Money is whole VND as <see cref="long"/>. There is no quantity, blob path
/// or admin field (BR-28).
/// </summary>
public sealed record ProductSummary(
    int Id,
    string Slug,
    string Name,
    BrandRef Brand,
    ConcentrationRef Concentration,
    ImageDto? PrimaryImage,
    long FromPrice,
    long? FromOriginalPrice,
    IReadOnlyList<decimal> VolumesMl,
    bool InStock,
    bool IsNew)
{
    public string Currency => Money.Currency;

    internal static ProductSummary From(ProductSummaryRow row, IImageStorage storage, DateTime newSince) =>
        new(
            row.Id,
            row.Slug,
            row.Name,
            new BrandRef(row.BrandSlug, row.BrandName),
            new ConcentrationRef(row.ConcentrationCode, row.ConcentrationName),
            row.PrimaryImageBlobPath is null
                ? null
                : new ImageDto(storage.GetPublicUrl(row.PrimaryImageBlobPath), row.PrimaryImageAltText ?? row.Name),
            row.FromPrice,
            row.FromOriginalPrice,
            row.VolumesMl,
            row.InStock,
            row.PublishedAtUtc is { } published && published >= newSince);
}
