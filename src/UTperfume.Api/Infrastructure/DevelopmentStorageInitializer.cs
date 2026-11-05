using UTperfume.Infrastructure.Storage;

namespace UTperfume.Api.Infrastructure;

/// <summary>
/// Development only: creates the image container in Azurite if it is missing. Runs in the background
/// and only logs a warning when storage is unreachable, so the app still starts without Azurite.
/// </summary>
internal sealed partial class DevelopmentStorageInitializer(
    AzureBlobImageStorage storage,
    ILogger<DevelopmentStorageInitializer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await storage.EnsureContainerExistsAsync(stoppingToken);
            LogReady(logger);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            LogUnavailable(logger, ex.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Image storage container is ready.")]
    private static partial void LogReady(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Image storage is not reachable ({ExceptionType}). Start Azurite with 'npx azurite --location .azurite --silent' to use uploads.")]
    private static partial void LogUnavailable(ILogger logger, string exceptionType);
}
