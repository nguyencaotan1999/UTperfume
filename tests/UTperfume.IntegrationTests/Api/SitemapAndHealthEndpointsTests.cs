using System.Net;
using System.Xml.Linq;
using UTperfume.Application.Domain;
using UTperfume.IntegrationTests.Infrastructure;

namespace UTperfume.IntegrationTests.Api;

public sealed class SitemapEndpointsTests(TestDatabase database) : CatalogApiTestBase(database)
{
    private static readonly XNamespace Ns = "http://www.sitemaps.org/schemas/sitemap/0.9";

    private async Task<(HttpResponseMessage Response, string[] Locs, XDocument Document)> GetSitemapAsync(HttpClient? client = null)
    {
        var response = await (client ?? Client).GetAsync("/sitemap.xml", Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        var document = XDocument.Parse(body);
        return (response, document.Descendants(Ns + "loc").Select(l => l.Value).ToArray(), document);
    }

    // AC-S3
    [Fact]
    public async Task Sitemap_lists_home_catalogue_visible_products_active_brands_and_active_categories_with_absolute_urls()
    {
        var active = NewBrand("dior");
        var emptyBrand = NewBrand("brand-without-products");
        var inactiveBrand = NewBrand("inactive-brand", active: false);
        var men = NewCategory("men");
        var emptyCategory = NewCategory("empty-category");
        var retired = NewCategory("retired", active: false);
        var visible = NewProduct(active, "sauvage", variants: NewVariant("S1", 50, 100));
        visible.Categories.Add(new ProductCategory { Category = men });
        await SaveAsync(
            visible,
            NewProduct(active, "draft", status: ProductStatus.Draft, variants: NewVariant("D1", 50, 100)),
            NewProduct(active, "archived", status: ProductStatus.Archived, variants: NewVariant("A1", 50, 100)),
            NewProduct(active, "no-active-variant", variants: NewVariant("N1", 50, 100, active: false)),
            NewProduct(inactiveBrand, "inactive-brand-product", variants: NewVariant("I1", 50, 100)),
            emptyBrand, emptyCategory, retired);

        var (response, locs, _) = await GetSitemapAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/xml");
        locs.Should().BeEquivalentTo(
        [
            "https://shop.test/",
            "https://shop.test/perfumes",
            "https://shop.test/perfumes/sauvage",
            "https://shop.test/brands/dior",
            "https://shop.test/brands/brand-without-products",
            "https://shop.test/categories/men",
            "https://shop.test/categories/empty-category",
        ]);
        locs.Should().OnlyContain(l => Uri.IsWellFormedUriString(l, UriKind.Absolute));
    }

    [Fact]
    public async Task Hidden_urls_never_appear()
    {
        var inactive = NewBrand("inactive", active: false);
        await SaveAsync(
            NewProduct(NewBrand("b"), "draft", status: ProductStatus.Draft, variants: NewVariant("D1", 50, 100)),
            NewProduct(inactive, "hidden-by-brand", variants: NewVariant("H1", 50, 100)),
            NewCategory("retired", active: false));

        var (_, locs, _) = await GetSitemapAsync();

        locs.Should().NotContain(l => l.Contains("draft") || l.Contains("hidden-by-brand") || l.Contains("inactive") || l.Contains("retired"));
    }

    [Fact]
    public async Task An_empty_catalogue_still_lists_home_and_the_catalogue()
    {
        var (response, locs, _) = await GetSitemapAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        locs.Should().Equal("https://shop.test/", "https://shop.test/perfumes");
    }

    [Fact]
    public async Task Sitemap_is_wellformed_xml_with_the_sitemap_namespace_and_lastmod_dates()
    {
        await SaveAsync(NewProduct(NewBrand("b"), "p", variants: NewVariant("P1", 50, 100)));

        var (_, _, document) = await GetSitemapAsync();

        document.Root!.Name.Should().Be(Ns + "urlset");
        document.Descendants(Ns + "lastmod").Select(l => l.Value).Should().OnlyContain(v => v == "2026-06-01");
    }

    [Fact]
    public async Task Urls_use_the_configured_base_not_a_forged_host_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/sitemap.xml");
        request.Headers.Host = "evil.example";

        var response = await Client.SendAsync(request, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        body.Should().Contain("https://shop.test/").And.NotContain("evil.example");
    }

    [Fact]
    public async Task Sitemap_is_anonymous_and_not_served_by_the_spa_fallback()
    {
        var response = await Client.GetAsync("/sitemap.xml", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).Should().NotContain("spa-index");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("shop.example.com")]
    [InlineData("/relative")]
    [InlineData("ftp://shop.example.com")]
    public void Startup_fails_outside_development_without_a_valid_public_base_url(string value)
    {
        using var factory = new ApiFactory(Database, new Dictionary<string, string?> { ["Store:PublicBaseUrl"] = value });

        var act = () => factory.CreateClient();

        act.Should().Throw<Microsoft.Extensions.Options.OptionsValidationException>()
            .WithMessage("*Store:PublicBaseUrl*absolute http or https URL*");
    }

    [Fact]
    public async Task Development_starts_without_a_public_base_url_and_uses_the_request_host()
    {
        using var factory = new ApiFactory(Database, new Dictionary<string, string?> { ["Store:PublicBaseUrl"] = "" }, environment: "Development");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/sitemap.xml", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("http://localhost/");
    }

    [Fact]
    public async Task Only_scheme_and_host_of_the_public_base_url_are_used()
    {
        using var factory = new ApiFactory(Database, new Dictionary<string, string?> { ["Store:PublicBaseUrl"] = "https://shop.example.com:8443/sub/path/" });
        using var client = factory.CreateClient();

        var body = await client.GetStringAsync("/sitemap.xml", Ct);

        body.Should().Contain("<loc>https://shop.example.com:8443/</loc>").And.NotContain("sub/path");
    }

    [Fact]
    public void Runtime_fallback_resolves_no_base_url_outside_development_so_the_endpoint_answers_500()
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var request = new Microsoft.AspNetCore.Http.DefaultHttpContext().Request;
        request.Scheme = "http";
        request.Host = new Microsoft.AspNetCore.Http.HostString("localhost");

        UTperfume.Api.Endpoints.SitemapEndpoints.ResolveBaseUrl(configuration, request, new FakeEnvironment("Production"))
            .Should().BeNull();
        UTperfume.Api.Endpoints.SitemapEndpoints.ResolveBaseUrl(configuration, request, new FakeEnvironment("Development"))
            .Should().Be("http://localhost");
    }

    // The cap is an option (Sitemap:MaxUrls, default and maximum 50,000) so the test can reach it without 50,001 rows.
    [Fact]
    public async Task Sitemap_is_capped_at_the_configured_maximum_and_keeps_home_and_catalogue_first()
    {
        var brand = NewBrand("cap-brand");
        var products = Enumerable.Range(1, 20)
            .Select(i => (object)NewProduct(brand, $"p-{i:00}", variants: NewVariant($"CAP-{i:00}", 50, 100)))
            .ToArray();
        await SaveAsync(products);
        using var factory = new ApiFactory(Database, new Dictionary<string, string?> { ["Sitemap:MaxUrls"] = "8" });
        using var client = factory.CreateClient();

        var (response, locs, _) = await GetSitemapAsync(client);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        locs.Should().HaveCount(8);
        locs.Take(3).Should().Equal("https://shop.test/", "https://shop.test/perfumes", "https://shop.test/brands/cap-brand");
        locs.Skip(3).Should().OnlyContain(l => l.StartsWith("https://shop.test/perfumes/p-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_cap_smaller_than_the_fixed_entries_truncates_them()
    {
        await SaveAsync(NewBrand("b"));
        using var factory = new ApiFactory(Database, new Dictionary<string, string?> { ["Sitemap:MaxUrls"] = "1" });
        using var client = factory.CreateClient();

        var (_, locs, _) = await GetSitemapAsync(client);

        locs.Should().Equal("https://shop.test/");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("50001")]
    [InlineData("-3")]
    public void Startup_fails_when_the_sitemap_cap_is_outside_1_to_50000(string value)
    {
        using var factory = new ApiFactory(Database, new Dictionary<string, string?> { ["Sitemap:MaxUrls"] = value });

        var act = () => factory.CreateClient();

        act.Should().Throw<Microsoft.Extensions.Options.OptionsValidationException>().WithMessage("*MaxUrls*");
    }

    private sealed class FakeEnvironment(string name) : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "test";

        public string ContentRootPath { get; set; } = ".";

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    [Fact]
    public async Task A_database_failure_gives_a_generic_500_problem_without_details()
    {
        using var factory = new ApiFactory(Database, new Dictionary<string, string?>
        {
            ["ConnectionStrings:Catalog"] = "Server=127.0.0.1,1;Database=Unreachable;Integrated Security=true;Connect Timeout=2;TrustServerCertificate=true",
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/sitemap.xml", Ct);

        ((int)response.StatusCode).Should().Be(500);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var body = await response.Content.ReadAsStringAsync(Ct);
        body.Should().NotContain("127.0.0.1").And.NotContain("SqlException").And.NotContain("Unreachable");
    }

    [Fact]
    public async Task Sitemap_xml_is_not_part_of_the_json_contract()
    {
        var document = await Client.GetStringAsync("/openapi/v1.json", Ct);

        document.Should().NotContain("sitemap");
    }
}

public sealed class HealthEndpointsTests(TestDatabase database) : CatalogApiTestBase(database)
{
    private const string Unreachable =
        "Server=127.0.0.1,1;Database=Unreachable;Integrated Security=true;Connect Timeout=2;TrustServerCertificate=true";

    // AC for E11
    [Fact]
    public async Task Ready_returns_200_when_the_database_is_reachable()
    {
        var response = await Client.GetAsync("/health/ready", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Be("Healthy");
    }

    [Fact]
    public async Task Ready_returns_503_when_the_database_is_unreachable_without_leaking_details()
    {
        using var factory = new ApiFactory(Database, new Dictionary<string, string?> { ["ConnectionStrings:Catalog"] = Unreachable });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadAsStringAsync(Ct);
        body.Should().Be("Unhealthy");
    }

    [Fact]
    public async Task Ready_returns_503_when_no_connection_string_is_configured()
    {
        using var factory = new ApiFactory(Database, new Dictionary<string, string?> { ["ConnectionStrings:Catalog"] = "" });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Live_stays_200_and_anonymous_when_the_database_is_down()
    {
        using var factory = new ApiFactory(Database, new Dictionary<string, string?> { ["ConnectionStrings:Catalog"] = Unreachable });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ready_is_anonymous_and_an_unknown_health_route_is_still_404()
    {
        (await Client.GetAsync("/health/ready", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client.GetAsync("/health/unknown", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Uptime monitors send HEAD.
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_routes_answer_HEAD_with_200(string path)
    {
        var response = await Client.SendAsync(new HttpRequestMessage(HttpMethod.Head, path), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ready_answers_HEAD_with_503_when_the_database_is_down()
    {
        using var factory = new ApiFactory(Database, new Dictionary<string, string?> { ["ConnectionStrings:Catalog"] = Unreachable });
        using var client = factory.CreateClient();

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/health/ready"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_routes_still_reject_POST_and_PUT_with_405_listing_get_and_head(string path)
    {
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete })
        {
            var response = await Client.SendAsync(new HttpRequestMessage(method, path), Ct);

            response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed, $"{method} {path}");
            response.Content.Headers.Allow.Should().BeEquivalentTo(["GET", "HEAD"]);
        }
    }

    [Fact]
    public async Task Ready_rejects_other_methods_with_405()
    {
        var response = await Client.PostAsync("/health/ready", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }
}
