namespace UTperfume.Application.Common;

/// <summary>
/// Machine-readable <c>code</c> values of admin ProblemDetails (requirements 0002 section 6.1, architecture section 6.2).
/// </summary>
public static class AdminErrorCodes
{
    public const string AntiforgeryInvalid = "antiforgery_invalid";
    public const string ConcurrencyConflict = "concurrency_conflict";
    public const string DuplicateSlug = "duplicate_slug";
    public const string DuplicateName = "duplicate_name";
    public const string DuplicateSku = "duplicate_sku";
    public const string DuplicateVolume = "duplicate_volume";
    public const string DuplicateEmail = "duplicate_email";
    public const string InUse = "in_use";
    public const string PublishRequirementsNotMet = "publish_requirements_not_met";
    public const string ProductWasPublished = "product_was_published";
    public const string PublishedProductRequirement = "published_product_requirement";
    public const string ImageLimitReached = "image_limit_reached";
    public const string UnsupportedImageType = "unsupported_image_type";
    public const string ImageTooLarge = "image_too_large";
    public const string StorageUnavailable = "storage_unavailable";
    public const string CannotDeactivateSelf = "cannot_deactivate_self";
    public const string LastActiveAdmin = "last_active_admin";
}
