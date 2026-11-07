namespace UTperfume.Application.Common;

/// <summary>
/// The image storage cannot be reached or answered with a server-side failure (outage, timeout, 5xx). The storage
/// adapter raises it so the Application layer does not depend on the storage SDK; the upload maps it to 503
/// <c>storage_unavailable</c>. The inner exception keeps the SDK detail for logs only.
/// </summary>
public sealed class ImageStorageUnavailableException : Exception
{
    public ImageStorageUnavailableException()
        : base("The image storage is unavailable.")
    {
    }

    public ImageStorageUnavailableException(string message)
        : base(message)
    {
    }

    public ImageStorageUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
