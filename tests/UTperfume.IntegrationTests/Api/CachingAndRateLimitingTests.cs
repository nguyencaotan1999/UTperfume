using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;
using UTperfume.IntegrationTests.Infrastructure;

namespace UTperfume.IntegrationTests.Api;

public sealed class CachingTests(TestDatabase database) : CatalogApiTestBase(database)
{
    private static string CacheControl(HttpResponseMessage response) =>
        response.Headers.CacheControl?.ToString() ?? string.Empty;

    private async Task SeedAsync() =>
        await SaveAsync(NewProduct(NewBrand("b"), "p", variants: NewVariant("P1", 50, 100)));

    // AC-X2
    [Theory]
    [InlineData("/api/v1/home")]
    [InlineData("/api/v1/brands")]
    [InlineData("/api/v1/brands/b")]
    [InlineData("/api/v1/categories")]
    [InlineData("/api/v1/products")]
    [InlineData("/api/v1/products?brand=b&sort=price_asc")]
    [InlineData("/api/v1/products/p")]
    [InlineData("/api/v1/products/p/related")]
    [InlineData("/api/v1/catalog/filters")]
    [InlineData("/sitemap.xml")]
    public async Task Successful_public_GET_responses_are_public_max_age_60(string url)
    {
        await SeedAsync();

        var response = await Client.GetAsync(url, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, url);
        CacheControl(response).Should().Be("public, max-age=60", url);
    }

    [Fact]
    public async Task A_cache_hit_carries_the_same_cache_control_header()
    {
        await SeedAsync();

        var first = await Client.GetAsync("/api/v1/brands", Ct);
        var second = await Client.GetAsync("/api/v1/brands", Ct);

        CacheControl(first).Should().Be("public, max-age=60");
        CacheControl(second).Should().Be("public, max-age=60");
    }


    [Theory]
    [InlineData("/api/v1/products/unknown", 404)]
    [InlineData("/api/v1/products?page=0", 400)]
    [InlineData("/api/v1/does-not-exist", 404)]
    [InlineData("/api/v1/admin/anything", 404)]
    public async Task Errors_and_admin_paths_are_never_publicly_cacheable(string url, int status)
    {
        var response = await Client.GetAsync(url, Ct);

        ((int)response.StatusCode).Should().Be(status);
        CacheControl(response).Should().Be("no-store");
    }

    [Fact]
    public async Task Wrong_method_responses_are_no_store()
    {
        var response = await Client.PostAsync("/api/v1/brands", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        CacheControl(response).Should().Be("no-store");
    }

    [Fact]
    public async Task Health_is_not_given_the_public_api_header()
    {
        var response = await Client.GetAsync("/health/live", Ct);

        CacheControl(response).Should().NotBe("public, max-age=60");
    }

    [Fact]
    public async Task The_server_caches_a_200_response_for_the_same_url()
    {
        await SeedAsync();
        var before = await Client.GetStringAsync("/api/v1/brands", Ct);

        await SaveAsync(NewBrand("added-later"));
        var after = await Client.GetStringAsync("/api/v1/brands", Ct);

        after.Should().Be(before, "the second response is served from the output cache");
        after.Should().NotContain("added-later");
    }

    [Fact]
    public async Task The_cache_key_includes_the_full_query_string()
    {
        await SeedAsync();
        var first = await Client.GetStringAsync("/api/v1/products?pageSize=24", Ct);
        await SaveAsync(NewProduct(NewBrand("c"), "q", variants: NewVariant("Q1", 50, 100)));

        var sameQuery = await Client.GetStringAsync("/api/v1/products?pageSize=24", Ct);
        var otherQuery = await Client.GetStringAsync("/api/v1/products?pageSize=23", Ct);
        var otherFilter = await Client.GetStringAsync("/api/v1/products?pageSize=24&brand=c", Ct);

        sameQuery.Should().Be(first);
        JsonDocument.Parse(otherQuery).RootElement.GetProperty("totalItems").GetInt32().Should().Be(2, "another query string is another cache entry");
        JsonDocument.Parse(otherFilter).RootElement.GetProperty("items").GetArrayLength().Should().Be(1);
    }


    // AC-P5 under caching: slugs are case-sensitive, so the cache key must be too.
    [Theory]
    [InlineData("/api/v1/products/p", "/api/v1/products/P")]
    [InlineData("/api/v1/brands/b", "/api/v1/brands/B")]
    public async Task An_uppercase_slug_is_not_served_from_the_cache_entry_of_the_lowercase_slug(string lower, string upper)
    {
        await SeedAsync();
        (await Client.GetAsync(lower, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await Client.GetAsync(upper, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
    [Fact]
    public async Task Errors_are_not_stored_so_a_later_valid_request_succeeds()
    {
        var missing = await Client.GetAsync("/api/v1/products/late", Ct);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await SaveAsync(NewProduct(NewBrand("b"), "late", variants: NewVariant("L1", 50, 100)));

        var found = await Client.GetAsync("/api/v1/products/late", Ct);

        found.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Evicting_the_catalog_tag_drops_cached_responses()
    {
        await SeedAsync();
        _ = await Client.GetStringAsync("/api/v1/brands", Ct);
        await SaveAsync(NewBrand("added-later"));

        await Factory.Services.GetRequiredService<IOutputCacheStore>().EvictByTagAsync("catalog", Ct);
        var after = await Client.GetStringAsync("/api/v1/brands", Ct);

        after.Should().Contain("added-later");
    }

    [Fact]
    public async Task A_request_with_an_authorization_header_is_not_stored_in_the_shared_cache()
    {
        await SeedAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/brands");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "x");
        _ = await Client.SendAsync(request, Ct);
        await SaveAsync(NewBrand("added-later"));

        var anonymous = await Client.GetStringAsync("/api/v1/brands", Ct);

        anonymous.Should().Contain("added-later", "a response for an authorized request must not have been cached for everyone");
    }
}

public sealed class RateLimitingTests(TestDatabase database) : CatalogApiTestBase(database)
{
    // AC-X3
    [Fact]
    public async Task Exceeding_the_public_limit_returns_429_problem_details_with_retry_after()
    {
        using var factory = new ApiFactory(Database, new Dictionary<string, string?> { ["RateLimiting:PublicPerMinute"] = "3" });
        using var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            (await client.GetAsync("/api/v1/products", Ct)).StatusCode.Should().Be(HttpStatusCode.OK, $"request {i + 1}");
        }

        var limited = await client.GetAsync("/api/v1/products", Ct);

        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        limited.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var json = JsonDocument.Parse(await limited.Content.ReadAsStringAsync(Ct)).RootElement;
        json.GetProperty("status").GetInt32().Should().Be(429);
        json.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
        limited.Headers.RetryAfter!.Delta!.Value.TotalSeconds.Should().BeInRange(1, 60);
        limited.Headers.CacheControl?.ToString().Should().Be("no-store");
    }

    [Fact]
    public async Task The_limit_covers_every_public_endpoint_and_the_sitemap_together()
    {
        using var factory = new ApiFactory(Database, new Dictionary<string, string?> { ["RateLimiting:PublicPerMinute"] = "3" });
        using var client = factory.CreateClient();

        (await client.GetAsync("/api/v1/home", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/v1/brands", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/sitemap.xml", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.GetAsync("/api/v1/categories", Ct)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await client.GetAsync("/sitemap.xml", Ct)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Cached_responses_still_count_against_the_limit()
    {
        using var factory = new ApiFactory(Database, new Dictionary<string, string?> { ["RateLimiting:PublicPerMinute"] = "2" });
        using var client = factory.CreateClient();

        (await client.GetAsync("/api/v1/brands", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/v1/brands", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.GetAsync("/api/v1/brands", Ct)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Health_endpoints_are_not_rate_limited()
    {
        using var factory = new ApiFactory(Database, new Dictionary<string, string?> { ["RateLimiting:PublicPerMinute"] = "1" });
        using var client = factory.CreateClient();

        for (var i = 0; i < 10; i++)
        {
            (await client.GetAsync("/health/live", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await client.GetAsync("/health/ready", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public void The_default_limits_are_120_public_and_5_auth_per_minute()
    {
        var options = Factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<UTperfume.Api.Infrastructure.RateLimitOptions>>().Value;

        options.PublicPerMinute.Should().Be(120);
        options.AuthPerMinute.Should().Be(5);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("100001")]
    public void Startup_fails_for_an_invalid_limit(string value)
    {
        using var factory = new ApiFactory(Database, new Dictionary<string, string?> { ["RateLimiting:PublicPerMinute"] = value });

        var act = () => factory.CreateClient();

        act.Should().Throw<Microsoft.Extensions.Options.OptionsValidationException>().WithMessage("*PublicPerMinute*");
    }

    [Fact]
    public async Task The_429_response_is_documented_in_the_openapi_contract()
    {
        var document = await Client.GetStringAsync("/openapi/v1.json", Ct);

        JsonDocument.Parse(document).RootElement.GetProperty("paths").GetProperty("/api/v1/brands").GetProperty("get")
            .GetProperty("responses").TryGetProperty("429", out _).Should().BeTrue();
    }
}
