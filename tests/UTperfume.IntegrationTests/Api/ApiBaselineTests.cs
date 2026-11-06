using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using UTperfume.IntegrationTests.Infrastructure;

namespace UTperfume.IntegrationTests.Api;

[Collection(DatabaseCollection.Name)]
public sealed class ApiBaselineTests(TestDatabase database) : IDisposable
{
    private readonly ApiFactory _factory = new(database);

    public void Dispose() => _factory.Dispose();

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.TryGetProperty("traceId", out var traceId).Should().BeTrue("AC-X1 requires a traceId");
        traceId.GetString().Should().NotBeNullOrWhiteSpace();
        return problem;
    }

    [Fact]
    public async Task Health_live_is_anonymous_and_returns_200()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Public_api_group_is_anonymous()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/health", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("status").GetString().Should().Be("ok");
    }

    // AC-S4
    [Theory]
    [InlineData("GET", "/api/v1/does-not-exist")]
    [InlineData("POST", "/api/v1/does-not-exist")]
    [InlineData("GET", "/api/unknown")]
    [InlineData("GET", "/api")]
    public async Task Unknown_api_route_returns_404_problem_details_not_index_html(string method, string path)
    {
        using var client = _factory.CreateClient();

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("status").GetInt32().Should().Be(404);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().NotContain("spa-index");
    }


    // ADR-0011 regression: a known path with the wrong method is 405 + Allow, not 404.
    [Theory]
    [InlineData("POST")]
    [InlineData("DELETE")]
    [InlineData("PUT")]
    public async Task Wrong_method_on_existing_route_returns_405_problem_details_with_allow(string method)
    {
        using var client = _factory.CreateClient();

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), "/api/v1/health"),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        response.Content.Headers.Allow.Should().Contain("GET").And.BeSubsetOf(["GET", "HEAD"]);
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("status").GetInt32().Should().Be(405);
    }

    [Fact]
    public async Task Unknown_api_route_with_get_still_returns_404_problem_details()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/does-not-exist", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.Allow.Should().BeEmpty();
        (await ReadProblemAsync(response)).GetProperty("status").GetInt32().Should().Be(404);
    }
    [Fact]
    public async Task Unknown_health_route_is_not_served_by_the_spa_fallback()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/unknown", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().NotContain("spa-index");
    }

    // AC-S4 (client routes)
    [Theory]
    [InlineData("/")]
    [InlineData("/perfumes")]
    [InlineData("/perfumes/some-slug")]
    [InlineData("/brands/unknown-brand")]
    public async Task Client_routes_fall_back_to_the_spa_index(string path)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Contain("spa-index");
    }

    // AC-X1
    [Fact]
    public async Task Unhandled_exception_returns_generic_500_problem_details_without_details()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddTransient<IStartupFilter, ThrowingStartupFilter>()));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/__throw", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("status").GetInt32().Should().Be(500);
        problem.TryGetProperty("detail", out _).Should().BeFalse();
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().NotContain(ThrowingStartupFilter.Secret).And.NotContain("StackTrace").And.NotContain(" at ");
    }


    // A BadHttpRequestException keeps its own status (a future upload limit gives 413 or 415), never a 500.
    [Theory]
    [InlineData(400, "Bad Request")]
    [InlineData(413, "Payload Too Large")]
    [InlineData(415, "Unsupported Media Type")]
    public async Task BadHttpRequestException_maps_to_its_own_status_with_problem_details_not_500(int status, string title)
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddTransient<IStartupFilter, ThrowingStartupFilter>()));
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/__status/{status}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be((HttpStatusCode)status);
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("status").GetInt32().Should().Be(status);
        problem.GetProperty("title").GetString().Should().Be(title);
        problem.TryGetProperty("detail", out _).Should().BeFalse("details are only shown in Development");
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().NotContain("client error detail");
    }
    [Fact]
    public async Task Security_headers_are_present_and_csp_allows_the_configured_image_host()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/health", TestContext.Current.CancellationToken);

        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle().Which.Should().Be("nosniff");
        response.Headers.GetValues("Referrer-Policy").Should().ContainSingle()
            .Which.Should().Be("strict-origin-when-cross-origin");
        var csp = response.Headers.GetValues("Content-Security-Policy").Should().ContainSingle().Subject;
        csp.Should().Contain("default-src 'self'")
            .And.Contain("img-src 'self' https://images.test data:")
            .And.Contain("script-src 'self'")
            .And.Contain("frame-ancestors 'none'");
    }

    [Fact]
    public async Task Security_headers_are_also_sent_on_error_responses()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/does-not-exist", TestContext.Current.CancellationToken);

        response.Headers.Contains("Content-Security-Policy").Should().BeTrue();
        response.Headers.Contains("X-Content-Type-Options").Should().BeTrue();
    }

    [Fact]
    public async Task Fallback_authorization_policy_requires_an_authenticated_user()
    {
        var provider = _factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        var fallback = await provider.GetFallbackPolicyAsync();

        fallback.Should().NotBeNull();
        fallback!.Requirements.Should().ContainSingle(r => r is Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement);
    }

    [Fact]
    public void Every_endpoint_is_explicitly_anonymous_or_requires_authorization()
    {
        var endpoints = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;

        endpoints.Should().NotBeEmpty();
        endpoints.Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null
                             && !e.Metadata.OfType<IAuthorizeData>().Any())
            .Select(e => e.DisplayName)
            .Should().BeEmpty("public endpoints must opt in to anonymous access, never rely on the fallback policy");
    }

    [Fact]
    public async Task Openapi_document_is_served_and_describes_the_public_contract()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var document = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        document.GetProperty("paths").TryGetProperty("/api/v1/health", out _).Should().BeTrue();
    }

    private sealed class ThrowingStartupFilter : IStartupFilter
    {
        public const string Secret = "boom-secret-detail";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            // The handler under test is registered in DI, so a second UseExceptionHandler in front of
            // the throwing middleware exercises the same GlobalExceptionHandler and ProblemDetails writer.
            app.UseExceptionHandler();
            app.UseWhen(
                context => context.Request.Path == "/api/v1/__throw",
                branch => branch.Run(_ => throw new InvalidOperationException(Secret)));
            app.UseWhen(
                context => context.Request.Path.StartsWithSegments("/api/v1/__status"),
                branch => branch.Run(context => throw new Microsoft.AspNetCore.Http.BadHttpRequestException(
                    "client error detail", int.Parse(context.Request.Path.Value!.Split('/')[^1], System.Globalization.CultureInfo.InvariantCulture))));
            next(app);
        };
    }
}
