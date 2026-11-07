using Microsoft.EntityFrameworkCore;
using UTperfume.Application.Common;
using UTperfume.Application.Domain;

namespace UTperfume.Application.Features.Admin.Images;

/// <summary>Admin view of a product image (0002 section 6.5). It carries the public URL, never the blob path (BR-A-22).</summary>
public sealed record AdminImage(
    int Id,
    string Url,
    string? AltText,
    bool IsPrimary,
    int SortOrder,
    string ContentType,
    int SizeBytes,
    int? Width,
    int? Height,
    string? OriginalFileName,
    DateTime CreatedAtUtc,
    string? CreatedBy);

/// <summary>The parsed multipart fields of <c>POST /products/{id}/images</c>.</summary>
public sealed record ProductImageUpload(ImageUploadFile? File, string? AltText, bool? IsPrimary);

/// <summary>
/// Product image upload and delete (P4-06; BR-A-15, BR-A-23 to BR-A-31). Both commands hold the product row lock for the
/// whole operation, so the count of images and the primary flag are consistent under concurrent requests (BR-A-25).
/// </summary>
public sealed class ProductImageCommands(
    ICatalogDbContext db,
    IProductWriteLock productLock,
    IAdminWriteSession session,
    IAdminAudit audit,
    IPublicCatalogCache cache,
    ImageUploadService uploads)
{
    /// <summary>The most images a product may have (D-A2, BR-A-25). The single named constant of the rule.</summary>
    public const int MaxImagesPerProduct = 6;

    public const int MaxAltTextLength = 250;

    public const string AltTextField = "altText";

    public async Task<Result<AdminImage>> UploadAsync(int productId, ProductImageUpload upload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(upload);

        var altText = NormalizeAltText(upload.AltText);
        if (altText?.Length > MaxAltTextLength)
        {
            return Result<AdminImage>.Invalid(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [AltTextField] = [$"The alt text must be at most {MaxAltTextLength} characters."],
            });
        }

        var validated = await uploads.ValidateAsync(upload.File, cancellationToken);
        if (!validated.IsSuccess)
        {
            return Result<AdminImage>.Failure(validated.Error!);
        }

        await using var transaction = await productLock.LockProductAsync(productId, cancellationToken);
        if (transaction is null)
        {
            return Result<AdminImage>.NotFound();
        }

        var existing = await db.ProductImages
            .Where(i => i.ProductId == productId)
            .Select(i => new { i.Id, i.SortOrder, i.IsPrimary })
            .ToListAsync(cancellationToken);
        if (existing.Count >= MaxImagesPerProduct)
        {
            return Result<AdminImage>.Unprocessable(AdminErrorCodes.ImageLimitReached);
        }

        var image = validated.Value!;
        var makePrimary = existing.Count == 0 || upload.IsPrimary == true;
        ProductImage? row = null;

        var stored = await uploads.StoreAsync(
            ImageOwner.Product,
            productId,
            image,
            async blobPath =>
            {
                if (makePrimary && existing.Any(i => i.IsPrimary))
                {
                    // The filtered unique index allows one primary: clear the old one first, in the same transaction.
                    var previous = await db.ProductImages.Where(i => i.ProductId == productId && i.IsPrimary).ToListAsync(cancellationToken);
                    foreach (var p in previous)
                    {
                        p.IsPrimary = false;
                    }

                    if (await session.SaveChangesAsync(cancellationToken) is { } clearError)
                    {
                        return Result<AdminImage>.Failure(clearError);
                    }
                }

                row = new ProductImage
                {
                    ProductId = productId,
                    BlobPath = blobPath,
                    ContentType = image.ContentType,
                    SizeBytes = image.Content.Length,
                    Width = image.Width,
                    Height = image.Height,
                    OriginalFileName = image.OriginalFileName,
                    AltText = altText,
                    SortOrder = existing.Count == 0 ? 0 : existing.Max(i => i.SortOrder) + 1,
                    IsPrimary = makePrimary,
                };
                db.ProductImages.Add(row);
                if (await session.SaveChangesAsync(cancellationToken) is { } error)
                {
                    return Result<AdminImage>.Failure(error);
                }

                await transaction.CommitAsync(cancellationToken);
                return Result<AdminImage>.Ok(ToDto(row));
            },
            replacedBlobPath: null,
            cancellationToken);

        if (stored.IsSuccess)
        {
            audit.Write(AdminAuditActions.ImageUpload, nameof(ProductImage), row!.Id);
            await cache.EvictAsync(cancellationToken);
        }

        return stored;
    }

    public async Task<Result<bool>> DeleteAsync(int productId, int imageId, CancellationToken cancellationToken)
    {
        await using var transaction = await productLock.LockProductAsync(productId, cancellationToken);
        if (transaction is null)
        {
            return Result<bool>.NotFound();
        }

        var images = await db.ProductImages
            .Where(i => i.ProductId == productId)
            .OrderBy(i => i.SortOrder).ThenBy(i => i.Id)
            .ToListAsync(cancellationToken);
        var image = images.Find(i => i.Id == imageId);
        if (image is null)
        {
            return Result<bool>.NotFound();
        }

        // BR-A-15: a Published product keeps at least one image.
        var published = await db.Products.AsNoTracking()
            .AnyAsync(p => p.Id == productId && p.Status == ProductStatus.Published, cancellationToken);
        if (published && images.Count == 1)
        {
            return Result<bool>.Unprocessable(AdminErrorCodes.PublishedProductRequirement);
        }

        db.ProductImages.Remove(image);
        if (await session.SaveChangesAsync(cancellationToken) is { } removeError)
        {
            return Result<bool>.Failure(removeError);
        }

        if (image.IsPrimary)
        {
            // BR-A-28: the remaining image with the lowest sortOrder, then the lowest id, becomes primary.
            var next = images.Find(i => i.Id != imageId);
            if (next is not null)
            {
                next.IsPrimary = true;
                if (await session.SaveChangesAsync(cancellationToken) is { } promoteError)
                {
                    return Result<bool>.Failure(promoteError);
                }
            }
        }

        await transaction.CommitAsync(cancellationToken);

        // Row first, blob afterwards; a failed blob delete is logged and leaves an orphan (ADR-0014, deletion).
        await uploads.DeleteQuietlyAsync(image.BlobPath, "image deleted");
        audit.Write(AdminAuditActions.ImageDelete, nameof(ProductImage), imageId);
        await cache.EvictAsync(cancellationToken);
        return Result<bool>.Ok(true);
    }

    private AdminImage ToDto(ProductImage i) => new(
        i.Id, uploads.GetPublicUrl(i.BlobPath), i.AltText, i.IsPrimary, i.SortOrder, i.ContentType, i.SizeBytes,
        i.Width, i.Height, i.OriginalFileName, i.CreatedAtUtc, i.CreatedBy);

    private static string? NormalizeAltText(string? altText) =>
        string.IsNullOrWhiteSpace(altText) ? null : altText.Trim();
}
