using Microsoft.EntityFrameworkCore;
using UTperfume.Infrastructure;
using UTperfume.Infrastructure.Persistence;

namespace UTperfume.Api.Infrastructure;

/// <summary>
/// Development only. Applies migrations and loads the sample catalogue in the background, so the app starts even when
/// SQL Server or Azurite is not available. It does nothing when no catalogue connection string is configured.
/// Migrations never run automatically outside Development (architecture section 11).
/// </summary>
public sealed partial class DevelopmentDataInitializer(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<DevelopmentDataInitializer> logger) : BackgroundService
{
    public const string EnabledKey = "DevelopmentSeed:Enabled";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!environment.IsDevelopment())
        {
            return; // Defence in depth: registration is already Development-only.
        }

        if (!configuration.GetValue(EnabledKey, true))
        {
            LogSkipped(logger, "disabled by configuration");
            return;
        }

        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString(DependencyInjection.CatalogConnectionName)))
        {
            LogSkipped(logger, "no catalogue connection string is configured");
            return;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync(stoppingToken);
            await scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>().SeedAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            LogFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Development data initialisation skipped: {Reason}.")]
    private static partial void LogSkipped(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Development data initialisation failed; the app keeps running.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
