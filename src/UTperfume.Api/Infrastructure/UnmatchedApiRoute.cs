using Microsoft.AspNetCore.Routing.Matching;
using Microsoft.AspNetCore.Routing.Template;

namespace UTperfume.Api.Infrastructure;

internal static class UnmatchedApiRoute
{
    /// <summary>
    /// Catch-all result for /api and /health. A path that exists for other HTTP methods answers 405 with an
    /// <c>Allow</c> header; a path that exists for none answers 404. Both bodies are written by the
    /// status-code pages middleware as ProblemDetails (ADR-0011).
    /// </summary>
    public static IResult Handle(HttpContext context, EndpointDataSource dataSource)
    {
        var allowed = new SortedSet<string>(StringComparer.Ordinal);
        var path = context.Request.Path;

        foreach (var endpoint in dataSource.Endpoints.OfType<RouteEndpoint>())
        {
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
            // The SPA fallback ("{*path:regex}") is constrained and must not count as a real route.
            if (methods is null || methods.Count == 0 || endpoint.RoutePattern.RawText?.StartsWith("{*", StringComparison.Ordinal) == true)
            {
                continue; // method-agnostic endpoints (the catch-alls) never define an Allow list
            }

            var matcher = new TemplateMatcher(
                TemplateParser.Parse(endpoint.RoutePattern.RawText ?? string.Empty),
                new RouteValueDictionary());
            if (matcher.TryMatch(path, new RouteValueDictionary()))
            {
                allowed.UnionWith(methods);
            }
        }

        if (allowed.Count == 0)
        {
            return Results.StatusCode(StatusCodes.Status404NotFound);
        }


        context.Response.Headers.Allow = string.Join(", ", allowed);
        return Results.StatusCode(StatusCodes.Status405MethodNotAllowed);
    }
}
