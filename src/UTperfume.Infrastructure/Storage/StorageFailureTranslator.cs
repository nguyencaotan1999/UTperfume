using Azure;
using UTperfume.Application.Common;

namespace UTperfume.Infrastructure.Storage;

/// <summary>
/// Decides whether an exception from the storage SDK means "storage is unavailable" (outage, timeout, 5xx) and wraps it
/// in <see cref="ImageStorageUnavailableException"/>. Client-side errors (4xx, bad arguments) are not translated: they are defects.
/// </summary>
public static class StorageFailureTranslator
{
    public static bool IsUnavailable(Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            RequestFailedException failed => failed.Status == 0 || failed.Status >= 500,
            HttpRequestException => true,
            TimeoutException => true,
            // A timeout surfaces as a cancellation that the caller did not ask for.
            OperationCanceledException => !cancellationToken.IsCancellationRequested,
            _ => false,
        };
    }

    public static ImageStorageUnavailableException ToUnavailable(Exception exception) =>
        new("The image storage is unavailable.", exception);
}
