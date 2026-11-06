using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using UTperfume.IntegrationTests.Infrastructure;

namespace UTperfume.IntegrationTests.Api;

/// <summary>
/// P4-01: the admin boundary. 0001 AC-A1 to AC-A4 and 0002 AC-A-05 / BR-A-05, with the test authentication scheme.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AdminBoundaryTests(TestDatabase database) : IDisposable
{
    private const string AdminPrefix = "/api/v1/admin";

    /// <summary>The five anonymous endpoints of BR-A-05 (method and route, relative to the admin prefix).</summary>
    private static readonly (string Method, string Route)[] AnonymousAllowlist =
    [
        ("GET", "/auth/antiforgery"),
        ("POST", "/auth/sign-in"),
        ("POST", "/auth/sign-out"),
        ("POST", "/auth/password-reset/request"),
        ("POST", "/auth/password-reset/confirm"),
    ];

    private readonly ApiFactory _factory = new(database, includeAdminTestRoutes: true);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _factory.Dispose();

    private HttpRequestMessage Get(string url, string? user = null, string? roles = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (user is not null)
        {
            request.Headers.Add(TestAuthHandler.UserHeader, user);
        }

        if (roles is not null)
        {
            request.Headers.Add(TestAuthHandler.RolesHeader, roles);
        }

        return request;
    }

    [Fact]
    public async Task AC_A1_AnonymousAdminRequest_Returns401ProblemDetails()
    {
        using var client = _factory.CreateClient();

        using var response = await client.SendAsync(Get($"{AdminPrefix}/me"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task AC_A1_AnonymousRequestToTestWriteRoute_Returns401()
    {
        using var client = _factory.CreateClient();

        using var response = await client.SendAsync(Get($"{AdminPrefix}/test/brands/1"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AC_A2_AuthenticatedWithoutAdminRole_Returns403()
    {
        using var client = _factory.CreateClient();

        using var noRole = await client.SendAsync(Get($"{AdminPrefix}/me", user: "7"), Ct);
        using var otherRole = await client.SendAsync(Get($"{AdminPrefix}/me", user: "7", roles: "Customer"), Ct);
        using var writeRoute = await client.SendAsync(Get($"{AdminPrefix}/test/brands/1", user: "7", roles: "Customer"), Ct);

        noRole.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        otherRole.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        writeRoute.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AC_A3_AdminReachesMeEndpoint_ReturnsIdentityFieldsOnly()
    {
        using var client = _factory.CreateClient();
        using var request = Get($"{AdminPrefix}/me", user: "42", roles: "Catalog.Admin");
        request.Headers.Add(TestAuthHandler.EmailHeader, "admin@example.test");

        using var response = await client.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl?.NoStore.Should().BeTrue();
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
        json.EnumerateObject().Select(p => p.Name).Should()
            .BeEquivalentTo("id", "email", "displayName", "roles");
        json.GetProperty("id").GetString().Should().Be("42");
        json.GetProperty("email").GetString().Should().Be("admin@example.test");
        json.GetProperty("roles").EnumerateArray().Select(e => e.GetString()).Should().Equal("Catalog.Admin");
    }

    [Fact]
    public async Task AC_A4_PublicEndpointWithoutCredentials_Succeeds()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/health", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void AC_A_05_EveryAdminEndpointRequiresAdminOnly_ExceptTheBrA05Allowlist()
    {
        var all = AllEndpoints();
        var endpoints = AdminEndpoints();

        endpoints.Should().Contain(e => Normalize(e) == "api/v1/admin/me");
        endpoints.Should().Contain(
            e => Normalize(e).StartsWith("api/v1/admin/test/", StringComparison.Ordinal),
            "the P4-08 test routes must be covered by the boundary check");

        FindBoundaryViolations(all).Should().BeEmpty(
            "every non-allowlisted admin endpoint must require AdminOnly and nothing else may match /api/v1/admin/*");
    }

    // M1 regression: a route outside the group, with or without a leading slash, must be reported.
    [Theory]
    [InlineData("api/v1/admin/test-escape")]
    [InlineData("/api/v1/admin/test-escape")]
    [InlineData("/API/V1/ADMIN/test-escape")]
    [InlineData("/api/v1/{area}/test-escape")]
    [InlineData("api/{version}/admin/{**rest}")]
    [InlineData("/{**anything}")]
    public void AC_A_05_BoundaryCheck_ReportsAnAdminRouteMappedOutsideTheGroup(string template)
    {
        var escape = BuildEndpoint(template, adminOnly: false);

        FindBoundaryViolations([escape]).Should().ContainSingle(v => v.Contains(template, StringComparison.Ordinal));
    }

    [Fact]
    public void AC_A_05_BoundaryCheck_AcceptsTheKnownCatchAllsAndAdminOnlyRoutes()
    {
        var endpoints = new[]
        {
            BuildEndpoint("/api/{**path}", adminOnly: false),
            BuildEndpoint("/health/{**path}", adminOnly: false),
            BuildEndpoint("{*path:regex(^(?!api(/|$)|health(/|$)).*$)}", adminOnly: false),
            BuildEndpoint("api/v1/admin/x", adminOnly: true),
            BuildEndpoint("/api/v1/products/{id}", adminOnly: false),
        };

        FindBoundaryViolations(endpoints).Should().BeEmpty();
    }

    [Fact]
    public void AC_A_05_EveryRealEndpointThatCanMatchAnAdminPath_IsInTheGroupOrTheApiCatchAll()
    {
        var matching = AllEndpoints().Where(e => CanMatchUnderAdmin(e.RoutePattern)).Select(Normalize).ToList();

        matching.Should().OnlyContain(t => t.StartsWith("api/v1/admin", StringComparison.OrdinalIgnoreCase) || t == "api/{**path}");
        matching.Should().Contain("api/{**path}");
    }

    [Fact]
    public void AC_A_05_AnonymousAdminEndpoints_AreExactlyTheBrA05Allowlist_OrFewer()
    {
        // Allowlisted routes are mapped by P4-03 and P4-05. Until then none may exist as anonymous endpoints that are
        // not on the list, and any that exist must really be anonymous and must not be outside the five.
        var anonymous = AdminEndpoints()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(Describe)
            .ToList();

        var allowed = AnonymousAllowlist.Select(a => $"{a.Method} {AdminPrefix}{a.Route}").ToList();
        anonymous.Should().BeSubsetOf(allowed);
        AnonymousAllowlist.Should().HaveCount(5);
    }

    [Fact]
    public async Task AC_A_05_ChangePasswordWithoutSession_Returns401()
    {
        // The route does not exist before P4-03. When it is added it must be AdminOnly; before that, the admin
        // group fallback still answers 401 or 404, never a successful anonymous response.
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync($"{AdminPrefix}/auth/change-password", new { }, Ct);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.NotFound);
        ((int)response.StatusCode).Should().NotBe(200);
    }

    // L9: the throwaway routes of P4-08 must not exist in a host built without them.
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    [InlineData("Testing")]
    public void P4_08_TestRoutesAreAbsentFromHostsThatDoNotOptIn(string environment)
    {
        using var factory = new ApiFactory(database, environment: environment, includeAdminTestRoutes: false);

        var routes = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Select(Normalize).ToList();

        routes.Should().Contain("api/v1/admin/me");
        routes.Should().NotContain(r => r.StartsWith("api/v1/admin/test", StringComparison.OrdinalIgnoreCase));
    }

    private List<RouteEndpoint> AllEndpoints() =>
        _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

    private List<RouteEndpoint> AdminEndpoints() => AllEndpoints().Where(IsLiteralAdminRoute).ToList();

    /// <summary>The template without its leading slash, so "api/v1/admin/x" and "/api/v1/admin/x" compare equal.</summary>
    private static string Normalize(RouteEndpoint endpoint) => (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/');

    private static bool IsLiteralAdminRoute(RouteEndpoint endpoint)
    {
        var text = Normalize(endpoint);
        var prefix = AdminPrefix.TrimStart('/');
        return text.Equals(prefix, StringComparison.OrdinalIgnoreCase)
            || text.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when the pattern can match a path under /api/v1/admin. Parameters are treated as matching (constraints other
    /// than a catch-all regex are ignored), so the answer errs on the side of reporting.
    /// </summary>
    private static bool CanMatchUnderAdmin(RoutePattern pattern)
    {
        string[] prefix = ["api", "v1", "admin"];
        string[] probe = ["api", "v1", "admin", "probe"];
        var segments = pattern.PathSegments;
        for (var i = 0; i < prefix.Length; i++)
        {
            if (i >= segments.Count)
            {
                return false;
            }

            var parts = segments[i].Parts;
            if (parts.Count == 1 && parts[0] is RoutePatternLiteralPart literal)
            {
                if (!literal.Content.Equals(prefix[i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                continue;
            }

            if (parts.Count == 1 && parts[0] is RoutePatternParameterPart { IsCatchAll: true } catchAll)
            {
                var remaining = string.Join('/', probe.Skip(i));
                foreach (var policy in catchAll.ParameterPolicies)
                {
                    if (policy.Content is { } content && content.StartsWith("regex(", StringComparison.Ordinal) && content.EndsWith(')')
                        && !System.Text.RegularExpressions.Regex.IsMatch(remaining, content[6..^1]))
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        return true;
    }

    private static RouteEndpoint BuildEndpoint(string template, bool adminOnly, string method = "GET")
    {
        var builder = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse(template), 0);
        builder.Metadata.Add(new HttpMethodMetadata([method]));
        if (adminOnly)
        {
            builder.Metadata.Add(new AuthorizeAttribute("AdminOnly"));
        }

        return (RouteEndpoint)builder.Build();
    }

    /// <summary>
    /// Boundary check over a set of endpoints: every route under the admin prefix (slash and case insensitive) is AdminOnly
    /// unless on the BR-A-05 allowlist, and no endpoint outside that prefix may match a path under it, except the
    /// <c>/api/{**path}</c> and <c>/health/{**path}</c> catch-alls and the SPA fallback that excludes api paths.
    /// </summary>
    private static List<string> FindBoundaryViolations(IEnumerable<RouteEndpoint> endpoints)
    {
        var violations = new List<string>();
        foreach (var endpoint in endpoints)
        {
            if (IsLiteralAdminRoute(endpoint))
            {
                if (IsAllowlisted(endpoint))
                {
                    continue;
                }

                var allowsAnonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
                var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).ToList();
                if (allowsAnonymous || !policies.Contains("AdminOnly"))
                {
                    violations.Add($"{Describe(endpoint)} (anonymous={allowsAnonymous}, policies=[{string.Join(",", policies)}])");
                }
            }
            else if (CanMatchUnderAdmin(endpoint.RoutePattern) && Normalize(endpoint) != "api/{**path}")
            {
                violations.Add($"{Describe(endpoint)} can match a path under {AdminPrefix} but is outside the admin group");
            }
        }

        return violations;
    }

    private static bool IsAllowlisted(RouteEndpoint endpoint)
    {
        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
        return AnonymousAllowlist.Any(a =>
            string.Equals(Normalize(endpoint), (AdminPrefix + a.Route).TrimStart('/'), StringComparison.OrdinalIgnoreCase)
            && methods.Contains(a.Method, StringComparer.OrdinalIgnoreCase));
    }

    private static string Describe(RouteEndpoint endpoint)
    {
        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
        return $"{string.Join("|", methods)} {endpoint.RoutePattern.RawText}";
    }
}
