using System.Security.Claims;
using Microsoft.AspNetCore.OutputCaching;
using UTperfume.Application.Common;

namespace UTperfume.Api.Infrastructure;

/// <summary>Registers the shared plumbing for admin writes (P4-08).</summary>
internal static class AdminWriteSetup
{
    public static IServiceCollection AddAdminWriteFoundation(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentAdmin, HttpContextCurrentAdmin>();
        services.AddScoped<IPublicCatalogCache, OutputCachePublicCatalogCache>();
        return services;
    }
}

/// <summary>Reads the acting admin from the authenticated principal (the Identity user id is the name identifier claim).</summary>
internal sealed class HttpContextCurrentAdmin(IHttpContextAccessor accessor) : ICurrentAdmin
{
    public string UserId =>
        accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } id
            ? id
            : throw new InvalidOperationException("An admin write requires an authenticated user.");

    public string? ClientIp => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string TraceId =>
        System.Diagnostics.Activity.Current?.Id ?? accessor.HttpContext?.TraceIdentifier ?? string.Empty;
}

/// <summary>
/// Evicts the <c>catalog</c> tag of the output cache (BR-A-21). The write is already committed when this runs, so a
/// failed eviction is logged and never turns the response into an error; the entries expire within 60 seconds anyway.
/// An <see cref="OperationCanceledException"/> (the client aborted the request) is deliberately not swallowed and propagates.
/// </summary>
internal sealed partial class OutputCachePublicCatalogCache(
    IOutputCacheStore store, ILogger<OutputCachePublicCatalogCache> logger) : IPublicCatalogCache
{
    public async Task EvictAsync(CancellationToken cancellationToken)
    {
        try
        {
            await store.EvictByTagAsync(PublicTrafficSetup.CacheTag, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogEvictionFailed(logger, exception);
        }
    }

    [LoggerMessage(EventId = 5001, Level = LogLevel.Warning, Message = "Evicting the public catalogue output cache failed; entries expire by age.")]
    private static partial void LogEvictionFailed(ILogger logger, Exception exception);
}
