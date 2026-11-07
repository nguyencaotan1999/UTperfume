using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UTperfume.Api.Infrastructure;
using UTperfume.IntegrationTests.Infrastructure;

namespace UTperfume.IntegrationTests.Api;

/// <summary>
/// P4-03: the antiforgery requirement on every admin POST, PUT, PATCH and DELETE (0002 BR-A-04, AC-A-05, AC-A-38),
/// with the real Identity cookie scheme and the P4-08 test routes.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AdminAntiforgeryTests : IAsyncLifetime, IDisposable
{
    private const string AdminPrefix = AdminIdentityTestSupport.AdminPrefix;
    private const string EmailA = "admin.a@example.test";
    private const string EmailB = "admin.b@example.test";

    private static readonly string[] UnsafeMethods = ["POST", "PUT", "PATCH", "DELETE"];

    private readonly TestDatabase _database;
    private readonly ApiFactory _factory;
    private readonly RequestReadProbe _probe = new();

    public AdminAntiforgeryTests(TestDatabase database)
    {
        _database = database;
        _factory = new ApiFactory(
            database,
            new Dictionary<string, string?> { ["RateLimiting:AuthPerMinute"] = "1000" },
            includeAdminTestRoutes: true,
            useTestAuthentication: false,
            configureServices: _probe.Install);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await AdminIdentityTestSupport.ResetIdentityAsync(_database);
        await _database.ResetCatalogAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void Dispose() => _factory.Dispose();

    // ---- AC-A-38: enumerated from the endpoint data source ------------------------------------------------------

    [Fact]
    public void AC_A_38_EveryAdminPostPutPatchDelete_CarriesTheAntiforgeryRequirement()
    {
        var unsafeEndpoints = AdminUnsafeEndpoints(_factory.Services.GetRequiredService<EndpointDataSource>().Endpoints);
        var described = unsafeEndpoints.Select(Describe).ToList();

        // The enumeration covers the real auth endpoints and the test routes (including the multipart one).
        described.Should().Contain($"POST {AdminPrefix}/auth/sign-in");
        described.Should().Contain($"POST {AdminPrefix}/auth/sign-out");
        described.Should().Contain($"POST {AdminPrefix}/test/brands");
        described.Should().Contain($"PUT {AdminPrefix}/test/brands/{{id:int}}");
        described.Should().Contain($"DELETE {AdminPrefix}/test/brands/{{id:int}}");
        described.Should().Contain($"POST {AdminPrefix}/test/uploads");

        FindMissingAntiforgery(unsafeEndpoints).Should().BeEmpty(
            "every admin POST, PUT, PATCH and DELETE endpoint must carry the antiforgery requirement");
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public void AC_A_38_TheEnumerationCheck_FailsForAnUnsafeAdminEndpointWithoutTheRequirement(string method)
    {
        var withoutRequirement = BuildEndpoint($"/api/v1/admin/x/{method.ToLowerInvariant()}", method, requireAntiforgery: false);
        var withRequirement = BuildEndpoint("/api/v1/admin/y", method, requireAntiforgery: true);
        var safe = BuildEndpoint("/api/v1/admin/z", "GET", requireAntiforgery: false);

        var unsafeEndpoints = AdminUnsafeEndpoints([withoutRequirement, withRequirement, safe]);

        unsafeEndpoints.Should().HaveCount(2, "the GET endpoint is not an unsafe method");
        FindMissingAntiforgery(unsafeEndpoints).Should().ContainSingle()
            .Which.Should().Contain($"{method} /api/v1/admin/x/");
    }

    // ---- BR-A-04, AC-A-05: behavior on the test routes ----------------------------------------------------------

    [Fact]
    public async Task AC_A_05_UnsafeAdminRequestWithoutTheToken_Returns400AntiforgeryInvalid_AndChangesNothing()
    {
        using var client = await SignedInClientAsync(EmailA);
        var xsrf = await client.FetchXsrfAsync(Ct);
        var id = await CreateBrandAsync(client, xsrf, "keep");
        var before = await BrandStateAsync(id);

        using var post = await SendAsync(client, HttpMethod.Post, "/test/brands", new TestBrandBody("New", Slug(), null), xsrf: null);
        using var put = await SendAsync(
            client, HttpMethod.Put, $"/test/brands/{id}", new TestBrandBody("Changed", null, before.RowVersion), xsrf: null);
        using var delete = await SendAsync(
            client, HttpMethod.Delete, $"/test/brands/{id}?rowVersion={Uri.EscapeDataString(before.RowVersion)}", null, xsrf: null);

        foreach (var response in new[] { post, put, delete })
        {
            await AssertAntiforgeryInvalidAsync(response);
        }

        (await BrandStateAsync(id)).Should().Be(before, "a rejected request changes nothing");
        (await CountMyBrandsAsync()).Should().Be(1, "the rejected POST created no brand");
    }

    [Fact]
    public async Task AC_A_05_UnsafeAdminRequestWithAForgedToken_Returns400AntiforgeryInvalid_AndChangesNothing()
    {
        using var client = await SignedInClientAsync(EmailA);
        var xsrf = await client.FetchXsrfAsync(Ct);
        var id = await CreateBrandAsync(client, xsrf, "keep");
        var before = await BrandStateAsync(id);

        using var forged = await SendAsync(client, HttpMethod.Post, "/test/brands", new TestBrandBody("New", Slug(), null), "forged-token");
        using var tampered = await SendAsync(
            client, HttpMethod.Put, $"/test/brands/{id}", new TestBrandBody("Changed", null, before.RowVersion), xsrf + "x");
        using var empty = await SendAsync(client, HttpMethod.Post, "/test/brands", new TestBrandBody("New", Slug(), null), string.Empty);

        foreach (var response in new[] { forged, tampered, empty })
        {
            await AssertAntiforgeryInvalidAsync(response);
        }

        (await BrandStateAsync(id)).Should().Be(before);
        (await CountMyBrandsAsync()).Should().Be(1);
    }

    [Fact]
    public async Task AC_A_05_UnsafeAdminRequestWithAValidToken_Succeeds()
    {
        using var client = await SignedInClientAsync(EmailA);
        var xsrf = await client.FetchXsrfAsync(Ct);

        var id = await CreateBrandAsync(client, xsrf, "Valid");
        var state = await BrandStateAsync(id);
        using var put = await SendAsync(
            client, HttpMethod.Put, $"/test/brands/{id}", new TestBrandBody("Renamed", null, state.RowVersion), xsrf);
        var updated = await BrandStateAsync(id);
        using var delete = await SendAsync(
            client, HttpMethod.Delete, $"/test/brands/{id}?rowVersion={Uri.EscapeDataString(updated.RowVersion)}", null, xsrf);

        put.StatusCode.Should().Be(HttpStatusCode.OK);
        updated.Name.Should().Be("Renamed");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task AC_A_05_ATokenFromAnotherSession_IsRejected()
    {
        using var clientA = await SignedInClientAsync(EmailA);
        using var clientB = await SignedInClientAsync(EmailB);
        var xsrfA = await clientA.FetchXsrfAsync(Ct);
        var xsrfB = await clientB.FetchXsrfAsync(Ct);

        using var crossed = await SendAsync(clientA, HttpMethod.Post, "/test/brands", new TestBrandBody("Cross", Slug(), null), xsrfB);
        using var own = await SendAsync(clientA, HttpMethod.Post, "/test/brands", new TestBrandBody("Own", Slug(), null), xsrfA);

        await AssertAntiforgeryInvalidAsync(crossed);
        own.StatusCode.Should().Be(HttpStatusCode.Created);
        (await CountMyBrandsAsync()).Should().Be(1, "only the request with the own token created a brand");
    }

    [Fact]
    public async Task AC_A_05_ASafeAdminRequest_DoesNotNeedTheToken()
    {
        using var client = await SignedInClientAsync(EmailA);

        using var me = await client.GetAsync($"{AdminPrefix}/me", Ct);
        using var missing = await client.GetAsync($"{AdminPrefix}/test/brands/999999", Ct);

        me.StatusCode.Should().Be(HttpStatusCode.OK);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AC_A_38_AMultipartAdminUpload_IsRejectedBeforeTheHandlerRuns_WithoutAValidToken()
    {
        using var client = await SignedInClientAsync(EmailA);
        var xsrf = await client.FetchXsrfAsync(Ct);
        var readsBefore = TestAdminRoutes.UploadFormReads;

        using var missing = await client.SendAsync(Multipart("/test/uploads", xsrf: null), Ct);
        using var forged = await client.SendAsync(Multipart("/test/uploads", "forged-token"), Ct);

        await AssertAntiforgeryInvalidAsync(missing);
        await AssertAntiforgeryInvalidAsync(forged);
        TestAdminRoutes.UploadFormReads.Should().Be(readsBefore, "the handler did not run, so it did not read the form");

        using var valid = await client.SendAsync(Multipart("/test/uploads", xsrf), Ct);

        valid.StatusCode.Should().Be(HttpStatusCode.OK);
        TestAdminRoutes.UploadFormReads.Should().Be(readsBefore + 1);
    }

    // ---- M1: the token is checked before binding ------------------------------------------------------------------

    [Fact]
    public async Task AC_A_38_AForgedTokenOnAJsonBoundRoute_IsRejectedBeforeTheBodyIsRead()
    {
        using var client = await SignedInClientAsync(EmailA);
        var xsrf = await client.FetchXsrfAsync(Ct);
        var runsBefore = TestAdminRoutes.JsonBindRuns;

        using var forged = await SendAsync(client, HttpMethod.Post, "/test/json-bind", new TestBrandBody("x", "y", null), "forged-token");

        await AssertAntiforgeryInvalidAsync(forged);
        TestAdminRoutes.JsonBindRuns.Should().Be(runsBefore, "the handler did not run");
        AssertBodyNotRead("/test/json-bind");

        // Control: the probe does see the body of a valid request, so a zero above is meaningful.
        using var valid = await SendAsync(client, HttpMethod.Post, "/test/json-bind", new TestBrandBody("x", "y", null), xsrf);
        valid.StatusCode.Should().Be(HttpStatusCode.OK);
        TestAdminRoutes.JsonBindRuns.Should().Be(runsBefore + 1);
        _probe.Last("POST", $"{AdminPrefix}/test/json-bind").BodyBytesRead.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AC_A_38_AForgedTokenOnAnIFormFileBoundRoute_IsRejectedBeforeTheFormIsRead()
    {
        using var client = await SignedInClientAsync(EmailA);
        var xsrf = await client.FetchXsrfAsync(Ct);
        var runsBefore = TestAdminRoutes.FileBindRuns;

        using var forged = await client.SendAsync(Multipart("/test/file-bind", "forged-token"), Ct);

        await AssertAntiforgeryInvalidAsync(forged);
        TestAdminRoutes.FileBindRuns.Should().Be(runsBefore, "the handler did not run");
        AssertBodyNotRead("/test/file-bind");

        using var valid = await client.SendAsync(Multipart("/test/file-bind", xsrf), Ct);
        valid.StatusCode.Should().Be(HttpStatusCode.OK);
        TestAdminRoutes.FileBindRuns.Should().Be(runsBefore + 1);
        var record = _probe.Last("POST", $"{AdminPrefix}/test/file-bind");
        record.FormParsed.Should().BeTrue("control: the probe sees a parsed form on a valid request");
        record.BodyBytesRead.Should().BeGreaterThan(0);
    }

    // ---- M2, M3: the token is accepted from the X-XSRF-TOKEN header only ------------------------------------------

    [Theory]
    [InlineData("/test/uploads")]
    [InlineData("/test/file-bind")]
    public async Task AC_A_38_AMultipartRequestWithAMissingHeader_IsRejectedWithoutReadingTheForm(string route)
    {
        using var client = await SignedInClientAsync(EmailA);
        await client.FetchXsrfAsync(Ct);
        var readsBefore = TestAdminRoutes.UploadFormReads + TestAdminRoutes.FileBindRuns;

        using var missing = await client.SendAsync(Multipart(route, xsrf: null), Ct);

        await AssertAntiforgeryInvalidAsync(missing);
        (TestAdminRoutes.UploadFormReads + TestAdminRoutes.FileBindRuns).Should().Be(readsBefore);
        AssertBodyNotRead(route);
    }

    [Theory]
    [InlineData("/test/uploads")]
    [InlineData("/test/file-bind")]
    public async Task BR_A_04_AValidTokenInAMultipartFormField_WithoutTheHeader_IsRejected(string route)
    {
        using var client = await SignedInClientAsync(EmailA);
        var xsrf = await client.FetchXsrfAsync(Ct);
        var runsBefore = TestAdminRoutes.UploadFormReads + TestAdminRoutes.FileBindRuns;

        var request = Multipart(route, xsrf: null, formField: ("__RequestVerificationToken", xsrf));
        using var response = await client.SendAsync(request, Ct);

        await AssertAntiforgeryInvalidAsync(response);
        (TestAdminRoutes.UploadFormReads + TestAdminRoutes.FileBindRuns).Should().Be(runsBefore, "the handler did not run");
        AssertBodyNotRead(route);
    }

    [Fact]
    public async Task BR_A_04_AValidTokenInAJsonBodyField_WithoutTheHeader_IsRejected()
    {
        using var client = await SignedInClientAsync(EmailA);
        var xsrf = await client.FetchXsrfAsync(Ct);
        var runsBefore = TestAdminRoutes.JsonBindRuns;

        using var response = await SendAsync(
            client, HttpMethod.Post, "/test/json-bind",
            new Dictionary<string, string> { ["name"] = "x", ["__RequestVerificationToken"] = xsrf, ["xsrf"] = xsrf }, xsrf: null);

        await AssertAntiforgeryInvalidAsync(response);
        TestAdminRoutes.JsonBindRuns.Should().Be(runsBefore);
        AssertBodyNotRead("/test/json-bind");
    }

    [Fact]
    public async Task BR_A_04_AValidTokenInAUrlEncodedFormField_WithoutTheHeader_IsRejected()
    {
        using var client = await SignedInClientAsync(EmailA);
        var xsrf = await client.FetchXsrfAsync(Ct);
        var runsBefore = TestAdminRoutes.UploadFormReads;

        var request = new HttpRequestMessage(HttpMethod.Post, $"{AdminPrefix}/test/uploads")
        {
            Content = new FormUrlEncodedContent([new("__RequestVerificationToken", xsrf)]),
        };
        using var response = await client.SendAsync(request, Ct);

        await AssertAntiforgeryInvalidAsync(response);
        TestAdminRoutes.UploadFormReads.Should().Be(runsBefore);
        AssertBodyNotRead("/test/uploads");
    }

    private void AssertBodyNotRead(string route)
    {
        var record = _probe.Last("POST", AdminPrefix + route);
        record.BodyBytesRead.Should().Be(0, "no byte of the body is read before the token is accepted");
        record.FormParsed.Should().BeFalse("the form is not parsed before the token is accepted");
    }

    // ---- M4: runtime sweep over every enumerated unsafe admin endpoint --------------------------------------------

    [Fact]
    public async Task AC_A_38_EveryEnumeratedUnsafeAdminEndpoint_RejectsASignedInRequestWithoutAToken_AndChangesNothing()
    {
        using var client = await SignedInClientAsync(EmailA);
        await client.FetchXsrfAsync(Ct);
        var endpoints = AdminUnsafeEndpoints(_factory.Services.GetRequiredService<EndpointDataSource>().Endpoints);
        endpoints.Should().NotBeEmpty();
        var before = await RowCountsAsync();

        foreach (var endpoint in endpoints)
        {
            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
            foreach (var method in methods is { Count: > 0 } ? methods.Where(IsUnsafeMethod) : ["POST"])
            {
                // The content type the endpoint declares (JSON or multipart), so it matches and the token check is what rejects the request.
                var request = new HttpRequestMessage(new HttpMethod(method), DummyPath(endpoint)) { Content = BodyFor(endpoint) };
                using var response = await client.SendAsync(request, Ct);

                await AssertAntiforgeryInvalidAsync(response, $"{method} {endpoint.RoutePattern.RawText}");
            }
        }

        (await RowCountsAsync()).Should().Be(before, "a rejected request changes nothing");
    }

    private static HttpContent BodyFor(RouteEndpoint endpoint)
    {
        var accepts = endpoint.Metadata.GetMetadata<IAcceptsMetadata>()?.ContentTypes ?? [];
        if (accepts.Any(c => c.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase)))
        {
            var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent([1, 2, 3]), "file", "a.png");
            return form;
        }

        return JsonContent.Create(new { });
    }

    private static bool IsUnsafeMethod(string method) => UnsafeMethods.Contains(method, StringComparer.OrdinalIgnoreCase);

    private async Task<(int Brands, int Variants, int Images)> RowCountsAsync()
    {
        await using var db = _database.CreateContext();
        return (await db.Brands.CountAsync(Ct), await db.ProductVariants.CountAsync(Ct), await db.ProductImages.CountAsync(Ct));
    }

    /// <summary>The route with every parameter replaced by a dummy value.</summary>
    private static string DummyPath(RouteEndpoint endpoint)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var segment in endpoint.RoutePattern.PathSegments)
        {
            builder.Append('/');
            foreach (var part in segment.Parts)
            {
                builder.Append(part switch
                {
                    RoutePatternLiteralPart literal => literal.Content,
                    RoutePatternSeparatorPart separator => separator.Content,
                    _ => "1",
                });
            }
        }

        return builder.ToString();
    }

    [Fact]
    public void AC_A_38_ARogueUnsafeRouteWithAParameterisedAdminPrefix_IsFlaggedByTheEnumeration()
    {
        using var rogue = new ApiFactory(
            _database,
            new Dictionary<string, string?> { ["RateLimiting:AuthPerMinute"] = "1000" },
            includeAdminTestRoutes: true,
            useTestAuthentication: false,
            configureServices: s => s.AddTransient<Microsoft.AspNetCore.Hosting.IStartupFilter, RogueRoutesStartupFilter>());

        var endpoints = AdminUnsafeEndpoints(rogue.Services.GetRequiredService<EndpointDataSource>().Endpoints);
        var missing = FindMissingAntiforgery(endpoints);

        missing.Should().Contain(m => m.Contains("/api/{v}/admin/rogue-param", StringComparison.Ordinal));
        missing.Should().Contain(m => m.Contains("rogue-regex", StringComparison.Ordinal));
        missing.Should().HaveCount(2, "only the two rogue routes lack the marker");
    }

    private sealed class RogueRoutesStartupFilter : Microsoft.AspNetCore.Hosting.IStartupFilter
    {
        public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(
            Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) =>
            app =>
            {
                next(app);
                app.UseEndpoints(e =>
                {
                    e.MapPost("/api/{v}/admin/rogue-param", () => Results.Ok()).AllowAnonymous();
                    e.MapPut("/api/v1/{area:regex(^admin$)}/rogue-regex", () => Results.Ok()).AllowAnonymous();
                });
            };
    }

    [Theory]
    [InlineData("/api/{v}/admin/rogue-param", true)]
    [InlineData("/api/v1/{area:regex(^admin$)}/rogue-regex", true)]
    [InlineData("/{a}/{b}/{c}/x", true)]
    [InlineData("/api/v1/{**rest}", true)]
    [InlineData("/api/{**rest}", true)]
    [InlineData("/{*all}", true)]
    [InlineData("/API/V1/ADMIN/shout", true)]
    [InlineData("api/v1/admin/no-leading-slash", true)]
    [InlineData("/api/v1/{area:regex(^shop$)}/x", false)]
    [InlineData("/api/v1/{id:int}/x", false)]
    [InlineData("/api/v1/brands/{id}", false)]
    [InlineData("/api/v1/administrators", false)]
    [InlineData("/api/{v}", false)]
    [InlineData("/{*path:regex(^(?!api(/|$)|health(/|$)).*$)}", false)]
    public void AC_A_38_TheAdminRouteDetection_ConsidersParameterisedAndRegexConstrainedPrefixes(string template, bool expectedAdmin)
    {
        var endpoint = BuildEndpoint(template, "POST", requireAntiforgery: false);

        IsAdminRoute(endpoint).Should().Be(expectedAdmin);
    }

    [Fact]
    public async Task AC_A_05_SignInWithAMissingOrForgedToken_StillReturns400AntiforgeryInvalid()
    {
        await AdminIdentityTestSupport.CreateUserAsync(_factory.Services, EmailA);
        using var client = _factory.CreateSecureClient();
        await client.FetchXsrfAsync(Ct);

        using var missing = await client.PostAsync("/auth/sign-in", new { email = EmailA, password = AdminIdentityTestSupport.Password }, null, Ct);
        using var forged = await client.PostAsync("/auth/sign-in", new { email = EmailA, password = AdminIdentityTestSupport.Password }, "forged", Ct);
        using var signOutMissing = await client.PostAsync("/auth/sign-out", null, null, Ct);

        await AssertAntiforgeryInvalidAsync(missing);
        await AssertAntiforgeryInvalidAsync(forged);
        await AssertAntiforgeryInvalidAsync(signOutMissing);
        using var me = await client.GetAsync($"{AdminPrefix}/me", Ct);
        me.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a rejected sign-in creates no session");
    }

    // ---- helpers --------------------------------------------------------------------------------------------------

    private async Task<HttpClient> SignedInClientAsync(string email)
    {
        await AdminIdentityTestSupport.CreateUserAsync(_factory.Services, email);
        var client = _factory.CreateSecureClient();
        using var signIn = await client.SignInAsync(email, AdminIdentityTestSupport.Password, Ct);
        signIn.StatusCode.Should().Be(HttpStatusCode.OK);
        return client;
    }

    private readonly string _prefix = $"xsrf-{Guid.NewGuid():N}-";

    private string Slug() => _prefix + Guid.NewGuid().ToString("N");

    private async Task<int> CountMyBrandsAsync()
    {
        await using var db = _database.CreateContext();
        return await db.Brands.CountAsync(b => b.Slug.StartsWith(_prefix), Ct);
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string route, object? body, string? xsrf)
    {
        var request = new HttpRequestMessage(method, AdminPrefix + route);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (xsrf is not null)
        {
            request.Headers.Add("X-XSRF-TOKEN", xsrf);
        }

        return client.SendAsync(request, Ct);
    }

    private static HttpRequestMessage Multipart(string route, string? xsrf, (string Name, string Value)? formField = null)
    {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent([1, 2, 3]), "file", "a.png");
        if (formField is { } field)
        {
            content.Add(new StringContent(field.Value), field.Name);
        }

        var request = new HttpRequestMessage(HttpMethod.Post, AdminPrefix + route) { Content = content };
        if (xsrf is not null)
        {
            request.Headers.Add("X-XSRF-TOKEN", xsrf);
        }

        return request;
    }

    private async Task<int> CreateBrandAsync(HttpClient client, string xsrf, string name)
    {
        using var response = await SendAsync(client, HttpMethod.Post, "/test/brands", new TestBrandBody(name, Slug(), null), xsrf);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
        return json.GetProperty("id").GetInt32();
    }

    private async Task<(string Name, string RowVersion)> BrandStateAsync(int id)
    {
        await using var db = _database.CreateContext();
        var brand = await db.Brands.AsNoTracking().SingleAsync(b => b.Id == id, Ct);
        return (brand.Name, UTperfume.Application.Common.AdminRowVersion.Encode(brand.RowVersion));
    }

    private static async Task AssertAntiforgeryInvalidAsync(HttpResponseMessage response, string? because = null)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, because);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json", because);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
        json.GetProperty("code").GetString().Should().Be("antiforgery_invalid", because);
    }

    private static readonly string[] AdminPrefixSegments = ["api", "v1", "admin"];

    // Constraints that can never match the text of a prefix segment such as "api" or "admin".
    private static readonly string[] NumericConstraints = ["int", "long", "guid", "bool", "double", "decimal", "float", "datetime"];

    /// <summary>
    /// The anonymous 404 handler for unmatched /api routes (<c>app.Map("/api/{**path}")</c> in Program.cs). A catch-all under
    /// /api can in theory match the admin prefix, but this route only ever answers 404 ProblemDetails and reads nothing.
    /// </summary>
    private const string UnmatchedApiRouteTemplate = "/api/{**path}";

    /// <summary>
    /// True for a route that could match a path under <c>/api/v1/admin</c>: literal segments compare ignoring case and any
    /// leading slash; a parameterised, composite or regex-constrained segment counts when its constraints allow the
    /// admin segment; a catch-all within the first three segments matches the rest of the prefix.
    /// </summary>
    private static bool IsAdminRoute(RouteEndpoint endpoint)
    {
        var raw = endpoint.RoutePattern.RawText ?? string.Empty;
        if (string.Equals(raw, UnmatchedApiRouteTemplate, StringComparison.Ordinal))
        {
            return false;
        }

        var segments = endpoint.RoutePattern.PathSegments;
        for (var i = 0; i < AdminPrefixSegments.Length; i++)
        {
            if (i >= segments.Count)
            {
                return false;
            }

            var segment = segments[i];
            if (segment.Parts is [RoutePatternParameterPart { IsCatchAll: true } catchAll])
            {
                return ConstraintsAllow(catchAll, string.Join('/', AdminPrefixSegments[i..]));
            }

            if (!SegmentCanMatch(segment, AdminPrefixSegments[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SegmentCanMatch(RoutePatternPathSegment segment, string value)
    {
        if (segment.Parts is [RoutePatternLiteralPart onlyLiteral])
        {
            return string.Equals(onlyLiteral.Content, value, StringComparison.OrdinalIgnoreCase);
        }

        if (segment.Parts is [RoutePatternParameterPart onlyParameter])
        {
            return ConstraintsAllow(onlyParameter, value);
        }

        // Composite segment such as "ad{x}": literal parts must fit, parameters match any non-empty text.
        var pattern = string.Concat(segment.Parts.Select(part => part switch
        {
            RoutePatternLiteralPart literal => System.Text.RegularExpressions.Regex.Escape(literal.Content),
            RoutePatternSeparatorPart separator => System.Text.RegularExpressions.Regex.Escape(separator.Content),
            _ => ".+",
        }));
        return System.Text.RegularExpressions.Regex.IsMatch(
            value, $"^{pattern}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    private static bool ConstraintsAllow(RoutePatternParameterPart parameter, string value)
    {
        foreach (var policy in parameter.ParameterPolicies)
        {
            var content = policy.Content ?? string.Empty;
            if (NumericConstraints.Contains(content, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            if (content.StartsWith("regex(", StringComparison.OrdinalIgnoreCase) && content.EndsWith(')'))
            {
                var pattern = content["regex(".Length..^1];
                if (!System.Text.RegularExpressions.Regex.IsMatch(
                        value, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Every endpoint under the admin prefix that accepts POST, PUT, PATCH or DELETE (or any method).</summary>
    private static List<RouteEndpoint> AdminUnsafeEndpoints(IEnumerable<Endpoint> endpoints) =>
    [
        .. endpoints.OfType<RouteEndpoint>().Where(IsAdminRoute).Where(e =>
        {
            var methods = e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
            return methods is null || methods.Count == 0 || methods.Any(m => UnsafeMethods.Contains(m, StringComparer.OrdinalIgnoreCase));
        }),
    ];

    private static List<string> FindMissingAntiforgery(IEnumerable<RouteEndpoint> endpoints) =>
    [
        .. endpoints
            .Where(e => e.Metadata.GetMetadata<RequiresAdminAntiforgeryMetadata>() is null)
            .Select(Describe),
    ];

    private static string Describe(RouteEndpoint endpoint)
    {
        var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [];
        return $"{string.Join("|", methods)} {endpoint.RoutePattern.RawText}";
    }

    private static RouteEndpoint BuildEndpoint(string template, string method, bool requireAntiforgery)
    {
        var builder = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse(template), 0);
        builder.Metadata.Add(new HttpMethodMetadata([method]));
        if (requireAntiforgery)
        {
            builder.Metadata.Add(RequiresAdminAntiforgeryMetadata.Instance);
        }

        return (RouteEndpoint)builder.Build();
    }
}
