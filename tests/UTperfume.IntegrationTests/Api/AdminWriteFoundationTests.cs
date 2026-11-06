using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using UTperfume.Application.Domain;
using UTperfume.IntegrationTests.Infrastructure;

namespace UTperfume.IntegrationTests.Api;

/// <summary>
/// P4-08: the shared admin write foundation, driven through endpoints that exist only in the test host.
/// Covers BR-A-10, BR-A-11, BR-A-21, BR-A-22 and BR-A-31 (mechanism), AC-A-14, AC-A-15, AC-A-26 and AC-A-39.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AdminWriteFoundationTests : IAsyncLifetime, IDisposable
{
    private const string Admin = "42";
    private static readonly DateTimeOffset ClockStart = new(2031, 5, 6, 7, 8, 9, 123, TimeSpan.Zero);
    private readonly TestDatabase database;
    private readonly FixedClock _clock = new(ClockStart);
    private readonly ApiFactory _factory;
    private HttpClient _client = null!;

    public AdminWriteFoundationTests(TestDatabase database)
    {
        this.database = database;
        // The fake clock is confined to this test host (M3).
        _factory = new ApiFactory(database, includeAdminTestRoutes: true, configureServices: s =>
        {
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(_clock);
        });
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await database.ResetCatalogAsync();
        _client = _factory.CreateClient();
    }

    public ValueTask DisposeAsync()
    {
        _client?.Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose() => _factory.Dispose();

    private HttpRequestMessage Admin_(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, "/api/v1/admin/test" + url);
        request.Headers.Add(TestAuthHandler.UserHeader, Admin);
        request.Headers.Add(TestAuthHandler.RolesHeader, "Catalog.Admin");
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private async Task<(HttpResponseMessage Response, JsonElement Json)> SendAsync(HttpRequestMessage request)
    {
        var response = await _client.SendAsync(request, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        var json = string.IsNullOrEmpty(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
        return (response, json);
    }

    private async Task<(int Id, string RowVersion)> CreateBrandAsync(string name, string slug)
    {
        var (response, json) = await SendAsync(Admin_(HttpMethod.Post, "/brands", new TestBrandBody(name, slug, null)));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (json.GetProperty("id").GetInt32(), json.GetProperty("rowVersion").GetString()!);
    }

    private static string Code(JsonElement json) => json.GetProperty("code").GetString()!;

    private static void AssertNoStore(HttpResponseMessage response) =>
        (response.Headers.CacheControl?.ToString() ?? string.Empty).Should().Be("no-store");

    private async Task<Product> SeedProductAsync(string slug = "p")
    {
        await using var db = database.CreateContext();
        var brand = new Brand { Name = "b-" + slug, Slug = "b-" + slug };
        var product = new Product { Brand = brand, ConcentrationId = 3, Name = slug, Slug = slug, Status = ProductStatus.Draft };
        db.Add(product);
        await db.SaveChangesAsync(Ct);
        return product;
    }

    // BR-A-10 / AC-A-15
    [Fact]
    public async Task BR_A_10_update_with_the_current_rowVersion_succeeds_and_returns_a_new_rowVersion()
    {
        var (id, version) = await CreateBrandAsync("Dior", "dior");

        var (response, json) = await SendAsync(Admin_(HttpMethod.Put, $"/brands/{id}", new TestBrandBody("Dior 2", null, version)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.GetProperty("rowVersion").GetString().Should().NotBe(version);
        await using var db = database.CreateContext();
        (await db.Brands.SingleAsync(b => b.Id == id, Ct)).Name.Should().Be("Dior 2");
    }

    [Fact]
    public async Task AC_A_15_stale_rowVersion_returns_409_concurrency_conflict_and_keeps_the_first_update()
    {
        var (id, version) = await CreateBrandAsync("Dior", "dior");
        (await SendAsync(Admin_(HttpMethod.Put, $"/brands/{id}", new TestBrandBody("First", null, version)))).Response
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var (response, json) = await SendAsync(Admin_(HttpMethod.Put, $"/brands/{id}", new TestBrandBody("Second", null, version)));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        Code(json).Should().Be("concurrency_conflict");
        json.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
        await using var db = database.CreateContext();
        (await db.Brands.SingleAsync(b => b.Id == id, Ct)).Name.Should().Be("First");
    }

    [Fact]
    public async Task BR_A_10_an_update_that_changes_no_field_still_detects_a_stale_rowVersion()
    {
        var (id, version) = await CreateBrandAsync("Dior", "dior");
        (await SendAsync(Admin_(HttpMethod.Put, $"/brands/{id}", new TestBrandBody("Changed", null, version)))).Response
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var (response, json) = await SendAsync(Admin_(HttpMethod.Put, $"/brands/{id}", new TestBrandBody(null, null, version)));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(json).Should().Be("concurrency_conflict");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not base64!")]
    public async Task BR_A_10_missing_or_malformed_rowVersion_returns_400_on_the_rowVersion_field(string? value)
    {
        var (id, _) = await CreateBrandAsync("Dior", "dior");

        var (response, json) = await SendAsync(Admin_(HttpMethod.Put, $"/brands/{id}", new TestBrandBody("X", null, value)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        json.GetProperty("errors").TryGetProperty("rowVersion", out _).Should().BeTrue();
        await using var db = database.CreateContext();
        (await db.Brands.SingleAsync(b => b.Id == id, Ct)).Name.Should().Be("Dior");
    }

    [Fact]
    public async Task BR_A_10_delete_with_a_stale_rowVersion_returns_409_and_deletes_nothing()
    {
        var (id, version) = await CreateBrandAsync("Dior", "dior");
        await SendAsync(Admin_(HttpMethod.Put, $"/brands/{id}", new TestBrandBody("Changed", null, version)));

        var (response, json) = await SendAsync(Admin_(HttpMethod.Delete, $"/brands/{id}?rowVersion={Uri.EscapeDataString(version)}"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(json).Should().Be("concurrency_conflict");
        await using var db = database.CreateContext();
        (await db.Brands.CountAsync(Ct)).Should().Be(1);
    }

    [Fact]
    public async Task BR_A_10_delete_with_the_current_rowVersion_returns_204()
    {
        var (id, version) = await CreateBrandAsync("Dior", "dior");

        var (response, _) = await SendAsync(Admin_(HttpMethod.Delete, $"/brands/{id}?rowVersion={Uri.EscapeDataString(version)}"));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // BR-A-11 / AC-A-14 / AC-A-26
    [Fact]
    public async Task AC_A_14_duplicate_brand_slug_returns_409_duplicate_slug()
    {
        await CreateBrandAsync("Dior", "dior");

        var (response, json) = await SendAsync(Admin_(HttpMethod.Post, "/brands", new TestBrandBody("Other", "dior", null)));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(json).Should().Be("duplicate_slug");
    }

    [Fact]
    public async Task BR_A_11_duplicate_brand_name_returns_409_duplicate_name()
    {
        await CreateBrandAsync("Dior", "dior");

        var (response, json) = await SendAsync(Admin_(HttpMethod.Post, "/brands", new TestBrandBody("Dior", "dior-2", null)));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(json).Should().Be("duplicate_name");
    }

    [Fact]
    public async Task BR_A_11_concurrent_inserts_of_the_same_slug_produce_one_row_and_one_409()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            SendAsync(Admin_(HttpMethod.Post, "/brands", new TestBrandBody("Name " + i, "same", null)))));

        results.Count(r => r.Response.StatusCode == HttpStatusCode.Created).Should().Be(1);
        results.Where(r => r.Response.StatusCode != HttpStatusCode.Created)
            .Should().OnlyContain(r => r.Response.StatusCode == HttpStatusCode.Conflict && Code(r.Json) == "duplicate_slug");
        await using var db = database.CreateContext();
        (await db.Brands.CountAsync(Ct)).Should().Be(1);
    }

    [Fact]
    public async Task AC_A_26_duplicate_sku_returns_409_duplicate_sku()
    {
        var first = await SeedProductAsync("p1");
        var second = await SeedProductAsync("p2");
        (await SendAsync(Admin_(HttpMethod.Post, $"/products/{first.Id}/variants", new TestVariantBody("AB-100", 50)))).Response
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var (response, json) = await SendAsync(Admin_(HttpMethod.Post, $"/products/{second.Id}/variants", new TestVariantBody("AB-100", 100)));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(json).Should().Be("duplicate_sku");
    }

    [Fact]
    public async Task BR_A_11_duplicate_volume_on_one_product_returns_409_duplicate_volume()
    {
        var product = await SeedProductAsync();
        await SendAsync(Admin_(HttpMethod.Post, $"/products/{product.Id}/variants", new TestVariantBody("A-1", 50)));

        var (response, json) = await SendAsync(Admin_(HttpMethod.Post, $"/products/{product.Id}/variants", new TestVariantBody("A-2", 50)));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(json).Should().Be("duplicate_volume");
    }

    // Unit-level check of the index table: duplicate_email is mapped (the identity table arrives with P4-02).
    [Theory]
    [InlineData("UX_Brand_Slug", "duplicate_slug")]
    [InlineData("UX_Brand_Name", "duplicate_name")]
    [InlineData("UX_Category_Slug", "duplicate_slug")]
    [InlineData("UX_Category_Name", "duplicate_name")]
    [InlineData("UX_Product_Slug", "duplicate_slug")]
    [InlineData("UX_ProductVariant_Sku", "duplicate_sku")]
    [InlineData("UX_ProductVariant_ProductId_VolumeMl", "duplicate_volume")]
    [InlineData("UX_FragranceNote_Slug", "duplicate_slug")]
    [InlineData("UX_FragranceNote_Name", "duplicate_name")]
    [InlineData("UserNameIndex", "duplicate_email")]
    [InlineData("EmailIndex", "duplicate_email")]
    [InlineData("UX_ProductImage_ProductId_Primary", "concurrency_conflict")]
    public void BR_A_11_every_unique_index_maps_to_its_code(string index, string code) =>
        UTperfume.Infrastructure.Persistence.AdminWriteSession.UniqueIndexCodes[index].Should().Be(code);

    // BR-A-31 / AC-A-39: audit stamping, against a clock the test controls
    [Fact]
    public async Task AC_A_39_create_and_update_stamp_UpdatedAtUtc_and_UpdatedBy_from_the_authenticated_admin()
    {
        var (id, version) = await CreateBrandAsync("Dior", "dior");
        await using (var db = database.CreateContext())
        {
            var created = await db.Brands.AsNoTracking().SingleAsync(b => b.Id == id, Ct);
            created.UpdatedBy.Should().Be(Admin);
            created.CreatedAtUtc.Should().Be(ClockStart.UtcDateTime);
            created.UpdatedAtUtc.Should().Be(ClockStart.UtcDateTime);
        }

        _clock.Advance(TimeSpan.FromHours(2));
        var request = Admin_(HttpMethod.Put, $"/brands/{id}", new TestBrandBody("Dior 2", null, version));
        request.Headers.Remove(TestAuthHandler.UserHeader);
        request.Headers.Add(TestAuthHandler.UserHeader, "99");
        (await SendAsync(request)).Response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var verify = database.CreateContext();
        var updated = await verify.Brands.AsNoTracking().SingleAsync(b => b.Id == id, Ct);
        updated.UpdatedBy.Should().Be("99");
        updated.UpdatedAtUtc.Should().Be(ClockStart.UtcDateTime.AddHours(2));
        updated.CreatedAtUtc.Should().Be(ClockStart.UtcDateTime);
    }

    [Fact]
    public async Task BR_A_31_an_inserted_image_gets_CreatedAtUtc_and_CreatedBy()
    {
        var product = await SeedProductAsync();

        var (response, json) = await SendAsync(Admin_(HttpMethod.Post, $"/products/{product.Id}/images"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        await using var db = database.CreateContext();
        var image = await db.ProductImages.AsNoTracking().SingleAsync(i => i.Id == json.GetProperty("id").GetInt32(), Ct);
        image.CreatedBy.Should().Be(Admin);
        image.CreatedAtUtc.Should().Be(ClockStart.UtcDateTime);
    }

    // M2: the row version is mandatory for modify and delete
    [Fact]
    public async Task BR_A_10_a_modify_without_ExpectRowVersion_fails_loudly_and_changes_nothing()
    {
        var (id, _) = await CreateBrandAsync("Dior", "dior");

        var (response, _) = await SendAsync(Admin_(HttpMethod.Put, $"/brands/{id}/unversioned", new TestBrandBody("Overwritten", null, null)));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        await using var db = database.CreateContext();
        (await db.Brands.SingleAsync(b => b.Id == id, Ct)).Name.Should().Be("Dior");
    }

    [Fact]
    public async Task BR_A_10_the_explicit_server_side_opt_out_still_saves()
    {
        var (id, _) = await CreateBrandAsync("Dior", "dior");

        var (response, _) = await SendAsync(Admin_(HttpMethod.Put, $"/brands/{id}/server-side", new TestBrandBody("Touched", null, null)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var db = database.CreateContext();
        var row = await db.Brands.SingleAsync(b => b.Id == id, Ct);
        row.Name.Should().Be("Touched");
        row.UpdatedBy.Should().Be(Admin);
    }

    // M5
    [Fact]
    public async Task An_unmapped_unique_index_violation_returns_a_500_problem_without_SQL_text()
    {
        var product = await SeedProductAsync();
        (await SendAsync(Admin_(HttpMethod.Post, $"/products/{product.Id}/images/fixed-path"))).Response
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await _client.SendAsync(Admin_(HttpMethod.Post, $"/products/{product.Id}/images/fixed-path"), Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        text.Should().NotContainEquivalentOf("UX_ProductImage").And.NotContainEquivalentOf("duplicate key")
            .And.NotContainEquivalentOf("INSERT").And.NotContainEquivalentOf("SqlException");
    }

    [Fact]
    public async Task BR_A_28_a_second_primary_image_for_a_product_returns_409_concurrency_conflict()
    {
        var product = await SeedProductAsync();
        (await SendAsync(Admin_(HttpMethod.Post, $"/products/{product.Id}/images/primary"))).Response
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var (response, json) = await SendAsync(Admin_(HttpMethod.Post, $"/products/{product.Id}/images/primary"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(json).Should().Be("concurrency_conflict");
    }

    // M4
    [Fact]
    public async Task BR_A_21_a_failing_cache_eviction_keeps_the_committed_write_and_logs_a_warning()
    {
        using var factory = new ApiFactory(
            database, includeAdminTestRoutes: true, configureServices: s =>
            {
                s.RemoveAll<IOutputCacheStore>();
                s.AddSingleton<IOutputCacheStore, ThrowingOutputCacheStore>();
            });
        using var client = factory.CreateClient();
        var created = await SendWithAsync(client, Admin_(HttpMethod.Post, "/brands", new TestBrandBody("Dior", "dior", null)));
        var id = created.GetProperty("id").GetInt32();
        var version = created.GetProperty("rowVersion").GetString();

        using var response = await client.SendAsync(Admin_(HttpMethod.Put, $"/brands/{id}", new TestBrandBody("Dior 2", null, version)), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var db = database.CreateContext();
        (await db.Brands.SingleAsync(b => b.Id == id, Ct)).Name.Should().Be("Dior 2");
        factory.Logs.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("Evicting the public catalogue output cache failed"));
    }

    private static async Task<JsonElement> SendWithAsync(HttpClient client, HttpRequestMessage request)
    {
        using var response = await client.SendAsync(request, Ct);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();
    }

    private sealed class ThrowingOutputCacheStore : IOutputCacheStore
    {
        public ValueTask EvictByTagAsync(string tag, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("cache store down");

        public ValueTask<byte[]?> GetAsync(string key, CancellationToken cancellationToken) => ValueTask.FromResult<byte[]?>(null);

        public ValueTask SetAsync(string key, byte[] value, string[]? tags, TimeSpan validFor, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class FixedClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    [Fact]
    public async Task BR_A_31_a_failed_write_does_not_stamp_or_change_the_row()
    {
        var (id, version) = await CreateBrandAsync("Dior", "dior");
        await using var before = database.CreateContext();
        var stamp = (await before.Brands.AsNoTracking().SingleAsync(b => b.Id == id, Ct)).UpdatedAtUtc;
        await SendAsync(Admin_(HttpMethod.Put, $"/brands/{id}", new TestBrandBody("Changed", null, version)));
        var stale = Admin_(HttpMethod.Put, $"/brands/{id}", new TestBrandBody("Late", null, version));
        stale.Headers.Remove(TestAuthHandler.UserHeader);
        stale.Headers.Add(TestAuthHandler.UserHeader, "77");

        await SendAsync(stale);

        await using var after = database.CreateContext();
        var row = await after.Brands.AsNoTracking().SingleAsync(b => b.Id == id, Ct);
        row.UpdatedBy.Should().Be(Admin);
        row.Name.Should().Be("Changed");
        row.UpdatedAtUtc.Should().BeOnOrAfter(stamp);
    }

    [Fact]
    public async Task BR_A_31_the_audit_event_has_action_entity_id_user_ip_and_trace_and_no_secrets()
    {
        var (id, version) = await CreateBrandAsync("Secret Name Inc", "secret");
        await SendAsync(Admin_(HttpMethod.Put, $"/brands/{id}", new TestBrandBody("Dior 2", null, version)));

        var events = _factory.Logs.Entries.Where(e => e.Message.StartsWith("Admin audit:", StringComparison.Ordinal)).ToList();

        events.Should().HaveCount(2);
        var update = events[^1];
        update.State["Action"].Should().Be("update");
        update.State["EntityType"].Should().Be("Brand");
        update.State["EntityId"].Should().Be(id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        update.State["UserId"].Should().Be(Admin);
        update.State.Should().ContainKey("ClientIp");
        update.State["TraceId"].Should().NotBeNull();
        update.Message.Should().NotContain("Dior 2").And.NotContain("Catalog.Admin").And.NotContain("@");
    }

    [Fact]
    public async Task BR_A_31_the_audit_event_is_written_only_after_a_successful_write()
    {
        var (id, version) = await CreateBrandAsync("Dior", "dior");
        await SendAsync(Admin_(HttpMethod.Put, $"/brands/{id}", new TestBrandBody("A", null, version)));
        var countAfterSuccess = _factory.Logs.Entries.Count(e => e.Message.StartsWith("Admin audit:", StringComparison.Ordinal));

        await SendAsync(Admin_(HttpMethod.Put, $"/brands/{id}", new TestBrandBody("B", null, version)));

        _factory.Logs.Entries.Count(e => e.Message.StartsWith("Admin audit:", StringComparison.Ordinal))
            .Should().Be(countAfterSuccess);
    }

    // BR-A-22 / BR-A-21
    [Fact]
    public async Task BR_A_22_every_admin_response_is_no_store_including_errors()
    {
        var (id, version) = await CreateBrandAsync("Dior", "dior");

        var responses = new[]
        {
            (await SendAsync(Admin_(HttpMethod.Get, $"/brands/{id}"))).Response,
            (await SendAsync(Admin_(HttpMethod.Get, "/brands/9999"))).Response,
            (await SendAsync(Admin_(HttpMethod.Put, $"/brands/{id}", new TestBrandBody("X", null, version)))).Response,
            (await SendAsync(Admin_(HttpMethod.Put, $"/brands/{id}", new TestBrandBody("Y", null, version)))).Response,
            (await SendAsync(Admin_(HttpMethod.Put, $"/brands/{id}", new TestBrandBody("Z", null, null)))).Response,
            await _client.GetAsync("/api/v1/admin/test/brands/1", Ct),
        };

        responses.Select(r => (int)r.StatusCode).Should().Equal(200, 404, 200, 409, 400, 401);
        foreach (var response in responses)
        {
            AssertNoStore(response);
        }
    }

    [Fact]
    public async Task Admin_routes_require_an_authenticated_user()
    {
        var response = await _client.GetAsync("/api/v1/admin/test/brands/1", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BR_A_21_a_successful_admin_write_evicts_the_cached_public_catalogue()
    {
        var (id, version) = await CreateBrandAsync("Dior", "dior");
        var before = await _client.GetStringAsync("/api/v1/brands", Ct);
        before.Should().Contain("Dior");

        (await SendAsync(Admin_(HttpMethod.Put, $"/brands/{id}", new TestBrandBody("Renamed Brand", null, version)))).Response
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await _client.GetStringAsync("/api/v1/brands", Ct);
        after.Should().Contain("Renamed Brand").And.NotContain("\"Dior\"");
    }

    [Fact]
    public async Task BR_A_21_a_rejected_admin_write_does_not_evict_the_cache()
    {
        var (id, version) = await CreateBrandAsync("Dior", "dior");
        await SendAsync(Admin_(HttpMethod.Put, $"/brands/{id}", new TestBrandBody("Dior", null, version)));
        _ = await _client.GetStringAsync("/api/v1/brands", Ct);
        await using (var db = database.CreateContext())
        {
            await db.Database.ExecuteSqlRawAsync("UPDATE catalog.Brand SET Name = N'Changed Behind The Cache'", Ct);
        }

        var stale = await SendAsync(Admin_(HttpMethod.Put, $"/brands/{id}", new TestBrandBody("Late", null, version)));
        var cached = await _client.GetStringAsync("/api/v1/brands", Ct);

        stale.Response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        cached.Should().NotContain("Changed Behind The Cache");
    }
}
