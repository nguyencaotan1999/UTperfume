using System.Globalization;
using System.Text;
using System.Xml;
using UTperfume.Application.Features.Sitemap;

namespace UTperfume.Api.Endpoints;

internal static class SitemapEndpoints
{
    public const string PublicBaseUrlKey = "Store:PublicBaseUrl";
    private const string SitemapNamespace = "http://www.sitemaps.org/schemas/sitemap/0.9";

    /// <summary>
    /// E10, anonymous and outside <c>/api/v1</c> because crawlers expect <c>/sitemap.xml</c>. Absolute URLs come from
    /// <c>Store:PublicBaseUrl</c>. The request host is trusted only in Development, so a forged Host header can never
    /// put another origin into a sitemap.
    /// </summary>
    public static IEndpointRouteBuilder MapSitemapEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/sitemap.xml", async (
                HttpContext context, SitemapQueries queries, IConfiguration configuration, IHostEnvironment environment,
                ILoggerFactory loggerFactory, CancellationToken ct) =>
            {
                var baseUrl = ResolveBaseUrl(configuration, context.Request, environment);
                if (baseUrl is null)
                {
                    loggerFactory.CreateLogger("Sitemap").LogError(
                        "Sitemap requested but '{Key}' is not configured with an absolute http(s) URL.", PublicBaseUrlKey);
                    return Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "An unexpected error occurred.");
                }

                var entries = await queries.GetEntriesAsync(ct);
                return Results.Text(Render(baseUrl, entries), "application/xml", Encoding.UTF8);
            })
            .AllowAnonymous()
            .RequireRateLimiting(UTperfume.Api.Infrastructure.PublicTrafficSetup.PolicyName)
            .CacheOutput(UTperfume.Api.Infrastructure.PublicTrafficSetup.PolicyName)
            .ExcludeFromDescription();

        return app;
    }

    internal static string? ResolveBaseUrl(IConfiguration configuration, HttpRequest request, IHostEnvironment environment)
    {
        var configured = configuration[PublicBaseUrlKey];
        if (Uri.TryCreate(configured, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            return uri.GetLeftPart(UriPartial.Authority);
        }

        return environment.IsDevelopment() ? $"{request.Scheme}://{request.Host}" : null;
    }

    internal static string Render(string baseUrl, IReadOnlyList<SitemapEntry> entries)
    {
        var builder = new StringBuilder();
        var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false), OmitXmlDeclaration = false };
        using (var writer = XmlWriter.Create(new Utf8StringWriter(builder), settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("urlset", SitemapNamespace);
            foreach (var entry in entries)
            {
                writer.WriteStartElement("url");
                writer.WriteElementString("loc", baseUrl + entry.Path);
                if (entry.LastModifiedUtc is { } modified)
                {
                    writer.WriteElementString("lastmod", modified.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return builder.ToString();
    }

    private sealed class Utf8StringWriter(StringBuilder builder) : StringWriter(builder, CultureInfo.InvariantCulture)
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}
