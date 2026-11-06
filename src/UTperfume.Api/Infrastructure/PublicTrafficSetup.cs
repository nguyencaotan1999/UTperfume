using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace UTperfume.Api.Infrastructure;

/// <summary>Output cache, Cache-Control and per-IP rate limiting for public endpoints (P2-06, AC-X2, AC-X3).</summary>
internal static class PublicTrafficSetup
{
    public const string PolicyName = "public";
    public const string CacheTag = "catalog";
    public const int PublicMaxAgeSeconds = 60;
    public const string PublicCacheControl = "public, max-age=60";

    public static IServiceCollection AddPublicTraffic(this IServiceCollection services)
    {
        services.AddOptions<RateLimitOptions>()
            .BindConfiguration(RateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // 60 s server cache for 200 responses, keyed by path and the full query string. The default base policy only
        // stores GET and HEAD, never a request with an Authorization header, and never a response that sets a cookie.
        // Admin writes will evict the tag (Phase 4).
        services.AddOutputCache(o =>
        {
            // Slugs are case-sensitive (AC-P5): /products/Sauvage must not be served from the entry of /products/sauvage.
            o.UseCaseSensitivePaths = true;
            o.AddPolicy(
                PolicyName,
                b => b.Expire(TimeSpan.FromSeconds(PublicMaxAgeSeconds)).SetVaryByQuery("*").Tag(CacheTag));
        });

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(PolicyName, context =>
            {
                var limit = context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value.PublicPerMinute;
                return RateLimitPartition.GetFixedWindowLimiter(
                    ClientKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limit,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    });
            });
            o.OnRejected = async (context, cancellationToken) =>
            {
                var retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? (int)Math.Ceiling(retryAfter.TotalSeconds)
                    : 60;
                var response = context.HttpContext.Response;
                response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
                response.StatusCode = StatusCodes.Status429TooManyRequests;
                await TypedResults.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too Many Requests")
                    .ExecuteAsync(context.HttpContext);
            };
        });

        return services;
    }

    /// <summary>
    /// The limiter partitions by the connection's remote address. Behind App Service the real client address needs
    /// forwarded headers (ASPNETCORE_FORWARDEDHEADERS_ENABLED), configured at deployment (Phase 5).
    /// </summary>
    private static string ClientKey(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// <summary>
    /// AC-X2: successful public GET and HEAD responses are <c>public, max-age=60</c>. Every other response from the API
    /// and every admin response is <c>no-store</c>, so an error or an admin payload is never cached by a browser or proxy.
    /// Runs outside the output cache, so cache hits also carry the header.
    /// </summary>
    public static IApplicationBuilder UseApiCacheControl(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            var path = context.Request.Path;
            if (path.StartsWithSegments("/api") || path.Equals("/sitemap.xml"))
            {
                context.Response.OnStarting(static state =>
                {
                    var http = (HttpContext)state;
                    var isAdmin = http.Request.Path.StartsWithSegments("/api/v1/admin");
                    var isRead = HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method);
                    http.Response.Headers.CacheControl =
                        !isAdmin && isRead && http.Response.StatusCode == StatusCodes.Status200OK
                            ? PublicCacheControl
                            : "no-store";
                    return Task.CompletedTask;
                }, context);
            }

            return next();
        });
}
