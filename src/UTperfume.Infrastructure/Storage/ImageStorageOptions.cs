using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace UTperfume.Infrastructure.Storage;

/// <summary>Bound from <c>Storage:Images</c>. In Azure only <see cref="ServiceUri"/> is set and Managed Identity is used.</summary>
public sealed class ImageStorageOptions
{
    public const string SectionName = "Storage:Images";

    /// <summary>Blob service endpoint, e.g. <c>https://account.blob.core.windows.net</c>. Used with Managed Identity.</summary>
    public string? ServiceUri { get; set; }

    /// <summary>Local Azurite only (<c>UseDevelopmentStorage=true</c>). Never set in Azure.</summary>
    public string? ConnectionString { get; set; }

    public string Container { get; set; } = "catalog-images";

    /// <summary>Base URL of the container as seen by browsers. A relative blob path is appended to it.</summary>
    public string? PublicBaseUrl { get; set; }
}

public sealed partial class ImageStorageOptionsValidator : IValidateOptions<ImageStorageOptions>
{
    public ValidateOptionsResult Validate(string? name, ImageStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ServiceUri) && string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            failures.Add("Storage:Images requires either ServiceUri or ConnectionString.");
        }

        if (!string.IsNullOrWhiteSpace(options.ServiceUri) && !IsAbsoluteHttpUri(options.ServiceUri))
        {
            failures.Add("Storage:Images:ServiceUri must be an absolute http(s) URI.");
        }

        if (string.IsNullOrWhiteSpace(options.Container) || !ContainerName().IsMatch(options.Container))
        {
            failures.Add("Storage:Images:Container must be 3-63 characters of lowercase letters, digits and single hyphens.");
        }

        if (string.IsNullOrWhiteSpace(options.PublicBaseUrl) || !IsAbsoluteHttpUri(options.PublicBaseUrl))
        {
            failures.Add("Storage:Images:PublicBaseUrl must be an absolute http(s) URL.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsAbsoluteHttpUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9]|-(?!-)){1,61}[a-z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex ContainerName();
}
