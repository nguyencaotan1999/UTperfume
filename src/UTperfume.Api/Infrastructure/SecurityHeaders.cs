namespace UTperfume.Api.Infrastructure;

internal static class SecurityHeaders
{
    /// <summary>
    /// Builds the Content-Security-Policy. The image host is the origin of
    /// <c>Storage:Images:PublicBaseUrl</c>; it is added to <c>img-src</c> only when it is an absolute http(s) URL.
    /// </summary>
    public static string BuildContentSecurityPolicy(string? imagePublicBaseUrl)
    {
        var imageSources = "'self'";
        if (Uri.TryCreate(imagePublicBaseUrl, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            var origin = uri.GetLeftPart(UriPartial.Authority);
            if (!string.Equals(origin, "null", StringComparison.Ordinal))
            {
                imageSources += " " + origin;
            }
        }

        return $"default-src 'self'; img-src {imageSources} data:; script-src 'self'; " +
               "style-src 'self' 'unsafe-inline'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    }

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, string contentSecurityPolicy) =>
        app.Use((context, next) =>
        {
            context.Response.OnStarting(static state =>
            {
                var (response, csp) = ((HttpResponse, string))state;
                var headers = response.Headers;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["Content-Security-Policy"] = csp;
                return Task.CompletedTask;
            }, (context.Response, contentSecurityPolicy));
            return next();
        });
}
