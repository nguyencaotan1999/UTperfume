using System.Globalization;
using Microsoft.Extensions.Logging;
using UTperfume.Application.Common;
using UTperfume.Application.Domain;

namespace UTperfume.Application.Features.Admin.Images;

/// <summary>The kind of owner an image belongs to; it decides the blob path prefix (BR-A-26).</summary>
public enum ImageOwner
{
    Product,
    Brand,
    Category,
    Banner,
}

/// <summary>An uploaded file as the API layer hands it over. The caller owns and disposes <see cref="Content"/>.</summary>
/// <param name="Content">The file bytes. Never trusted to be the declared length.</param>
/// <param name="Length">The length the request claims.</param>
/// <param name="ContentType">The declared content type.</param>
/// <param name="FileName">The client file name. Display only; never used in a path or a header.</param>
public sealed record ImageUploadFile(Stream Content, long Length, string? ContentType, string? FileName);

/// <summary>An upload that passed every check of BR-A-23 and BR-A-24 and is ready to be stored.</summary>
public sealed record ValidatedImage(
    byte[] Content, string ContentType, string Extension, int? Width, int? Height, string? OriginalFileName);

/// <summary>
/// The reusable image upload service (P4-06, BR-A-23 to BR-A-27, architecture section 7.4). It validates a file before
/// anything is stored, generates the server-side blob path, writes the blob first, runs the caller's database step, and
/// deletes the new blob again when that step fails. Product images (P4-06), brand logos (P4-10), category images (P4-11)
/// and banner images (P4-16) all use it; for the one-image owners the caller passes the previous blob path and this
/// service deletes it after the database step succeeded.
/// </summary>
public sealed partial class ImageUploadService(IImageStorage storage, ILogger<ImageUploadService> logger)
{
    /// <summary>The file size limit of BR-A-24 (5 MB, 5,242,880 bytes).</summary>
    public const int MaxFileBytes = ProductImage.MaxSizeBytes;

    /// <summary>The field name that carries the file in validation errors.</summary>
    public const string FileField = "file";

    private const int MaxOriginalFileNameLength = 255;

    /// <summary>
    /// Checks the file in this order: missing or empty (400), too large (413), declared type allowed and equal to the
    /// signature-detected type (415). Nothing is stored here.
    /// </summary>
    public async Task<Result<ValidatedImage>> ValidateAsync(ImageUploadFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length <= 0)
        {
            return FileInvalid("A file is required.");
        }

        if (file.Length > MaxFileBytes)
        {
            return Result<ValidatedImage>.PayloadTooLarge(AdminErrorCodes.ImageTooLarge);
        }

        // Read at most one byte more than the limit: the claimed length is not trusted.
        var buffer = new byte[MaxFileBytes + 1];
        var read = 0;
        int n;
        while (read < buffer.Length
               && (n = await file.Content.ReadAsync(buffer.AsMemory(read), cancellationToken)) > 0)
        {
            read += n;
        }

        if (read == 0)
        {
            return FileInvalid("A file is required.");
        }

        if (read > MaxFileBytes)
        {
            return Result<ValidatedImage>.PayloadTooLarge(AdminErrorCodes.ImageTooLarge);
        }

        var content = buffer.AsSpan(0, read);
        var detected = ImageHeader.Detect(content);
        if (detected is null || !string.Equals(Normalize(file.ContentType), detected.ContentType, StringComparison.Ordinal))
        {
            return Result<ValidatedImage>.UnsupportedMediaType(AdminErrorCodes.UnsupportedImageType);
        }

        var (width, height) = ImageHeader.ReadDimensions(detected, content);
        return Result<ValidatedImage>.Ok(new ValidatedImage(
            content.ToArray(), detected.ContentType, detected.Extension, width, height, CleanFileName(file.FileName)));
    }

    /// <summary>
    /// Writes the blob at a server-generated path, then runs <paramref name="persist"/> (the database step). When it
    /// fails, throws or is cancelled, the new blob is deleted (best effort, logged) and the failure is returned or rethrown.
    /// On success the optional <paramref name="replacedBlobPath"/> is deleted (best effort, logged).
    /// </summary>
    public async Task<Result<T>> StoreAsync<T>(
        ImageOwner owner,
        int ownerId,
        ValidatedImage image,
        Func<string, Task<Result<T>>> persist,
        string? replacedBlobPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(persist);

        var blobPath = BuildPath(owner, ownerId, image.Extension);
        try
        {
            using var stream = new MemoryStream(image.Content, writable: false);
            await storage.UploadAsync(blobPath, stream, image.ContentType, cancellationToken);
        }
        catch (ImageStorageUnavailableException exception)
        {
            // Nothing was stored and no database step ran. The detail stays in the log, never in the response.
            LogStorageUnavailable(logger, exception, blobPath);
            return Result<T>.ServiceUnavailable(AdminErrorCodes.StorageUnavailable);
        }

        Result<T> result;
        try
        {
            result = await persist(blobPath);
        }
        catch
        {
            await DeleteQuietlyAsync(blobPath, "compensation");
            throw;
        }

        if (!result.IsSuccess)
        {
            await DeleteQuietlyAsync(blobPath, "compensation");
            return result;
        }

        if (replacedBlobPath is not null)
        {
            await DeleteQuietlyAsync(replacedBlobPath, "replaced image");
        }

        return result;
    }

    /// <summary>Deletes a blob after the row is gone. A failure is logged and never fails the request (ADR-0014, deletion).</summary>
    public async Task DeleteQuietlyAsync(string blobPath, string reason)
    {
        try
        {
            // Not tied to the request: the database change is committed, so the delete should still be attempted.
            await storage.DeleteAsync(blobPath, CancellationToken.None);
        }
        catch (Exception exception)
        {
            LogBlobDeleteFailed(logger, exception, reason, blobPath);
        }
    }

    public string GetPublicUrl(string blobPath) => storage.GetPublicUrl(blobPath);

    /// <summary><c>products/{id}/{guid}.{ext}</c> and the matching paths of the other owners (BR-A-26). The client file name is never used.</summary>
    public static string BuildPath(ImageOwner owner, int ownerId, string extension)
    {
        var prefix = owner switch
        {
            ImageOwner.Product => "products",
            ImageOwner.Brand => "brands",
            ImageOwner.Category => "categories",
            ImageOwner.Banner => "banners",
            _ => throw new ArgumentOutOfRangeException(nameof(owner)),
        };
        return string.Create(CultureInfo.InvariantCulture, $"{prefix}/{ownerId}/{Guid.NewGuid():N}.{extension}");
    }

    private static Result<ValidatedImage> FileInvalid(string message) =>
        Result<ValidatedImage>.Invalid(new Dictionary<string, string[]>(StringComparer.Ordinal) { [FileField] = [message] });

    private static string Normalize(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return string.Empty;
        }

        var semicolon = contentType.IndexOf(';', StringComparison.Ordinal);
        return (semicolon >= 0 ? contentType[..semicolon] : contentType).Trim().ToLowerInvariant();
    }

    private static string? CleanFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var cleaned = new string(fileName.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (cleaned.Length > MaxOriginalFileNameLength)
        {
            cleaned = cleaned[..MaxOriginalFileNameLength];
        }

        return cleaned.Length == 0 ? null : cleaned;
    }

    [LoggerMessage(EventId = 5011, Level = LogLevel.Error,
        Message = "The image storage was unavailable during an upload; nothing was stored: {BlobPath}")]
    private static partial void LogStorageUnavailable(ILogger logger, Exception exception, string blobPath);

    [LoggerMessage(EventId = 5010, Level = LogLevel.Warning,
        Message = "Deleting the blob failed ({Reason}); it stays as an orphan until cleanup: {BlobPath}")]
    private static partial void LogBlobDeleteFailed(ILogger logger, Exception exception, string reason, string blobPath);
}
