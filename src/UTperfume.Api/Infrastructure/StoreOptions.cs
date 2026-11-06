using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace UTperfume.Api.Infrastructure;

/// <summary>Bound from <c>Store</c>. <c>PublicBaseUrl</c> is the public origin used for absolute URLs (sitemap).</summary>
public sealed class StoreOptions
{
    public const string SectionName = "Store";

    public string? PublicBaseUrl { get; set; }
}

/// <summary>
/// Outside Development <c>Store:PublicBaseUrl</c> is required and must be an absolute http(s) URL, so a missing
/// App Service setting fails the start-up with a clear message instead of failing later at the first sitemap request.
/// Only the scheme and host are used; the app cannot be served under a sub-path (ADR-0012).
/// </summary>
public sealed class StoreOptionsValidator(IHostEnvironment environment) : IValidateOptions<StoreOptions>
{
    public ValidateOptionsResult Validate(string? name, StoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (environment.IsDevelopment() && string.IsNullOrWhiteSpace(options.PublicBaseUrl))
        {
            return ValidateOptionsResult.Success; // Development falls back to the request host.
        }

        return IsAbsoluteHttpUrl(options.PublicBaseUrl)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                "Store:PublicBaseUrl must be set to the public origin as an absolute http or https URL, " +
                "for example https://shop.example.com (no path).");
    }

    internal static bool IsAbsoluteHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}

/// <summary>Bound from <c>RateLimiting</c> (architecture section 6.1). Limits are per client IP per minute.</summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    [Range(1, 100_000)]
    public int PublicPerMinute { get; set; } = 120;

    /// <summary>Sign-in and password reset (Phase 4). Validated now so a bad value fails at start-up.</summary>
    [Range(1, 100_000)]
    public int AuthPerMinute { get; set; } = 5;
}
