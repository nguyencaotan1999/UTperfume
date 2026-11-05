namespace UTperfume.Infrastructure.Storage;

public static class ImagePublicUrl
{
    /// <summary>Joins <paramref name="publicBaseUrl"/> and a relative blob path, escaping each path segment.</summary>
    /// <exception cref="ArgumentException">The path is empty, rooted, or contains backslashes, dot segments or empty segments.</exception>
    public static string Build(string publicBaseUrl, string blobPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicBaseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(blobPath);

        if (blobPath.Contains('\\', StringComparison.Ordinal))
        {
            throw new ArgumentException("Blob path must use '/' separators.", nameof(blobPath));
        }

        var segments = blobPath.Split('/');
        if (segments.Any(static s => s.Length == 0 || s is "." or ".."))
        {
            throw new ArgumentException("Blob path must be relative and contain no empty or dot segments.", nameof(blobPath));
        }

        return publicBaseUrl.TrimEnd('/') + "/" + string.Join('/', segments.Select(Uri.EscapeDataString));
    }
}
