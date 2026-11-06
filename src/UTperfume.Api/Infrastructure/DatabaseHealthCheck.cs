using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using UTperfume.Infrastructure.Persistence;

namespace UTperfume.Api.Infrastructure;

/// <summary>
/// Readiness probe for E11. Any failure, including a missing connection string, is reported as Unhealthy (503) and
/// logged without the connection string or the exception text in the response.
/// </summary>
internal sealed partial class DatabaseHealthCheck(IServiceScopeFactory scopeFactory, ILogger<DatabaseHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("The database is not reachable.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogUnavailable(logger, ex.GetType().Name);
            return HealthCheckResult.Unhealthy("The database is not reachable.");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Readiness check failed: the database is unavailable ({ExceptionType}).")]
    private static partial void LogUnavailable(ILogger logger, string exceptionType);
}
