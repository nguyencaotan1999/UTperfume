using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using UTperfume.Api.Infrastructure;
using UTperfume.Infrastructure.Identity;
using UTperfume.IntegrationTests.Infrastructure;

namespace UTperfume.IntegrationTests.Api;

/// <summary>
/// P4-02: admin sign-in, sign-out, session cookie and antiforgery token endpoints with the real Identity cookie scheme.
/// 0001 AC-A6 to AC-A8; 0002 BR-A-01 to BR-A-03, the sign-in and sign-out events of BR-A-31, AC-A-01 to AC-A-04, AC-A-06
/// and AC-A-40, and the sign-in part of BR-A-04.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AdminAuthTests : IAsyncLifetime, IDisposable
{
    private const string Email = "first.admin@example.test";

    private readonly TestDatabase _database;
    private readonly TestClock _clock = new();
    private readonly CountingPasswordHasher _hasher = new();
    private readonly ApiFactory _factory;
    private readonly ApiFactory _limitedFactory;
    private HttpClient _client = null!;

    public AdminAuthTests(TestDatabase database)
    {
        _database = database;
        void Services(IServiceCollection s)
        {
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(_clock);
            s.RemoveAll<IPasswordHasher<AdminUser>>();
            s.AddSingleton<IPasswordHasher<AdminUser>>(_hasher);
        }

        // A high limit, so tests that sign in many times are not throttled. The limit itself is tested separately.
        _factory = new ApiFactory(
            database,
            new Dictionary<string, string?> { ["RateLimiting:AuthPerMinute"] = "1000" },
            useTestAuthentication: false,
            configureServices: Services);
        _limitedFactory = new ApiFactory(
            database,
            new Dictionary<string, string?> { ["RateLimiting:AuthPerMinute"] = "3" },
            useTestAuthentication: false);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await AdminIdentityTestSupport.ResetIdentityAsync(_database);
        _client = _factory.CreateSecureClient();
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        _factory.Dispose();
        _limitedFactory.Dispose();
    }

    private Task<int> CreateAdminAsync(string email = Email, string password = AdminIdentityTestSupport.Password, bool active = true) =>
        AdminIdentityTestSupport.CreateUserAsync(_factory.Services, email, password, admin: true, active: active, displayName: "First Admin");

    private async Task<AdminUser> LoadUserAsync(int id)
    {
        await using var db = _database.CreateContext();
        return await db.Users.AsNoTracking().SingleAsync(u => u.Id == id, Ct);
    }

    private async Task<HttpResponseMessage> GetMeAsync(HttpClient? client = null) =>
        await (client ?? _client).GetAsync($"{AdminIdentityTestSupport.AdminPrefix}/me", Ct);

    private static async Task<(int Status, string? Type, string? Title, string? Detail)> ProblemOf(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
        string? Read(string name) => json.TryGetProperty(name, out var p) ? p.GetString() : null;
        return ((int)response.StatusCode, Read("type"), Read("title"), Read("detail"));
    }

    // ---- AC-A-01, BR-A-01, BR-A-02, AC-A8 (cookie) ------------------------------------------------------------

    [Fact]
    public async Task AC_A_01_SignInWithValidCredentials_Returns200AdminMeAndSetsTheHostCookie()
    {
        var id = await CreateAdminAsync();

        using var response = await _client.SignInAsync(Email, AdminIdentityTestSupport.Password, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl?.NoStore.Should().BeTrue();
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
        json.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("id", "email", "displayName", "roles");
        json.GetProperty("id").GetString().Should().Be(id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        json.GetProperty("email").GetString().Should().Be(Email);
        json.GetProperty("displayName").GetString().Should().Be("First Admin");
        json.GetProperty("roles").EnumerateArray().Select(e => e.GetString()).Should().Equal("Catalog.Admin");

        var cookie = response.SetCookies("__Host-utp-admin").Should().ContainSingle().Subject;
        var attributes = AdminIdentityTestSupport.CookieAttributes(cookie);
        attributes.Should().Contain("httponly").And.Contain("secure").And.Contain("samesite=strict").And.Contain("path=/");
        attributes.Should().NotContain(a => a.StartsWith("domain=", StringComparison.OrdinalIgnoreCase));
        cookie.Should().NotContain("password", "the response never echoes the password");
    }

    [Fact]
    public async Task BR_A_02_TheSessionCookieIsNotPersistent_NoRememberMe()
    {
        await CreateAdminAsync();

        using var response = await _client.SignInAsync(Email, AdminIdentityTestSupport.Password, Ct);

        var attributes = AdminIdentityTestSupport.CookieAttributes(response.SetCookies("__Host-utp-admin").Single());
        attributes.Should().NotContain(a => a.StartsWith("expires=", StringComparison.OrdinalIgnoreCase));
        attributes.Should().NotContain(a => a.StartsWith("max-age=", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BR_A_01_TheSignedInCookieOpensTheAdminArea()
    {
        await CreateAdminAsync();
        (await _client.SignInAsync(Email, AdminIdentityTestSupport.Password, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var me = await GetMeAsync();

        me.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonDocument.Parse(await me.Content.ReadAsStringAsync(Ct)).RootElement;
        json.GetProperty("email").GetString().Should().Be(Email);
        json.GetProperty("displayName").GetString().Should().Be("First Admin");
        json.GetProperty("roles").EnumerateArray().Select(e => e.GetString()).Should().Equal("Catalog.Admin");
    }

    [Theory]
    [InlineData("  FIRST.ADMIN@EXAMPLE.TEST  ")]
    [InlineData("First.Admin@Example.Test")]
    public async Task BR_A_01_TheEmailIsTrimmedAndComparedCaseInsensitively(string typed)
    {
        await CreateAdminAsync();

        using var response = await _client.SignInAsync(typed, AdminIdentityTestSupport.Password, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("", "x", "email")]
    [InlineData("   ", "x", "email")]
    [InlineData("a@example.test", "", "password")]
    public async Task BR_A_01_MissingEmailOrPassword_Returns400ValidationProblem(string email, string password, string field)
    {
        using var response = await _client.SignInAsync(email, password, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
        json.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue();
    }

    [Fact]
    public async Task BR_A_01_OverlongEmailOrPassword_Returns400WithoutHashing()
    {
        using var email = await _client.SignInAsync(new string('a', 257) + "@example.test", "x", Ct);
        using var password = await _client.SignInAsync("a@example.test", new string('p', 257), Ct);

        email.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        password.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- AC-A7, AC-A-02, AC-A6: one identical 401 --------------------------------------------------------------

    [Fact]
    public async Task AC_A7_UnknownEmailWrongPasswordDeactivatedAndNonAdmin_ReturnTheSame401()
    {
        await CreateAdminAsync();
        await AdminIdentityTestSupport.CreateUserAsync(_factory.Services, "inactive@example.test", active: false);
        await AdminIdentityTestSupport.CreateUserAsync(_factory.Services, "customer@example.test", admin: false);

        using var unknown = await _client.SignInAsync("nobody@example.test", AdminIdentityTestSupport.Password, Ct);
        using var wrong = await _client.SignInAsync(Email, "Wrong-Password-1234", Ct);
        // AC-A-02: a deactivated admin with the correct password.
        using var inactive = await _client.SignInAsync("inactive@example.test", AdminIdentityTestSupport.Password, Ct);
        // BR-A-01: a user without Catalog.Admin cannot sign in to the admin area.
        using var nonAdmin = await _client.SignInAsync("customer@example.test", AdminIdentityTestSupport.Password, Ct);

        var expected = await ProblemOf(wrong);
        expected.Status.Should().Be(401);
        (await ProblemOf(unknown)).Should().Be(expected);
        (await ProblemOf(inactive)).Should().Be(expected);
        (await ProblemOf(nonAdmin)).Should().Be(expected);
        foreach (var response in new[] { unknown, wrong, inactive, nonAdmin })
        {
            response.SetCookies("__Host-utp-admin").Should().BeEmpty();
            response.Headers.Contains("WWW-Authenticate").Should().BeFalse();
        }
    }

    [Fact]
    public async Task AC_A_02_ADeactivatedAdminWithTheCorrectPasswordIsNotSignedIn()
    {
        await CreateAdminAsync(active: false);

        using var response = await _client.SignInAsync(Email, AdminIdentityTestSupport.Password, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await GetMeAsync()).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AC_A6_AfterFiveFailures_TheCorrectPasswordIsStillRefusedWithTheSame401()
    {
        var id = await CreateAdminAsync();
        HttpResponseMessage? firstWrong = null;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var response = await _client.SignInAsync(Email, "Wrong-Password-1234", Ct);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"failure {attempt}");
            firstWrong ??= response;
        }

        using var correct = await _client.SignInAsync(Email, AdminIdentityTestSupport.Password, Ct);

        correct.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ProblemOf(correct)).Should().Be(await ProblemOf(firstWrong!), "the lockout is not revealed");
        correct.SetCookies("__Host-utp-admin").Should().BeEmpty();
        var user = await LoadUserAsync(id);
        user.LockoutEnd.Should().NotBeNull();
        user.LockoutEnd!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(15), TimeSpan.FromMinutes(1));
        firstWrong!.Dispose();
    }

    [Fact]
    public async Task AC_A6_FourFailuresDoNotLockTheAccount()
    {
        await CreateAdminAsync();
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            using var wrong = await _client.SignInAsync(Email, "Wrong-Password-1234", Ct);
            wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        using var correct = await _client.SignInAsync(Email, AdminIdentityTestSupport.Password, Ct);

        correct.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task BR_A_01_PasswordPolicyAndLockoutSettingsMatchTheApprovedValues()
    {
        var options = _factory.Services.GetRequiredService<IOptions<IdentityOptions>>().Value;

        options.Password.RequiredLength.Should().Be(12);
        options.Password.RequireDigit.Should().BeTrue();
        options.Password.RequireUppercase.Should().BeTrue();
        options.Password.RequireLowercase.Should().BeTrue();
        options.Password.RequireNonAlphanumeric.Should().BeFalse();
        options.Password.RequiredUniqueChars.Should().Be(4);
        options.Lockout.MaxFailedAccessAttempts.Should().Be(5);
        options.Lockout.DefaultLockoutTimeSpan.Should().Be(TimeSpan.FromMinutes(15));
        options.Lockout.AllowedForNewUsers.Should().BeTrue();
        options.User.RequireUniqueEmail.Should().BeTrue();
        options.SignIn.RequireConfirmedAccount.Should().BeFalse();
        await Task.CompletedTask;
    }

    [Theory]
    [InlineData("Abcdefghi12", false)] // 11 characters
    [InlineData("Abcdefghij12", true)] // 12 characters, no symbol needed
    [InlineData("abcdefghij12", false)] // no upper case
    [InlineData("ABCDEFGHIJ12", false)] // no lower case
    [InlineData("Abcdefghijkl", false)] // no digit
    [InlineData("Aa1Aa1Aa1Aa1", false)] // only 3 distinct characters
    [InlineData("Aa1Bb1Aa1Bb1", true)] // 4 distinct characters
    public async Task BR_A_01_PasswordPolicy_AcceptsAndRejectsAsApproved(string password, bool accepted)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
        var email = $"policy.{Guid.NewGuid():N}@example.test";

        var result = await users.CreateAsync(
            new AdminUser { UserName = email, Email = email, DisplayName = "Policy", CreatedAtUtc = DateTime.UtcNow }, password);

        result.Succeeded.Should().Be(accepted, string.Join("; ", result.Errors.Select(e => e.Code)));
        await AdminIdentityTestSupport.ResetIdentityAsync(_database);
    }

    [Fact]
    public async Task BR_A_01_TheStoredPasswordIsAnIdentityHash_NotThePassword()
    {
        var id = await CreateAdminAsync();

        var user = await LoadUserAsync(id);

        user.PasswordHash.Should().NotBeNullOrEmpty().And.NotContain(AdminIdentityTestSupport.Password);
        user.LockoutEnabled.Should().BeTrue("lockout is enabled for new users");
        user.SecurityStamp.Should().NotBeNullOrEmpty();
    }

    // ---- sign-out, AC-A-03, AC-A-04, AC-A8 ---------------------------------------------------------------------

    [Fact]
    public async Task AC_A_03_SignOutClearsTheSession_AndTheNextMeCallReturns401()
    {
        await CreateAdminAsync();
        (await _client.SignInAsync(Email, AdminIdentityTestSupport.Password, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetMeAsync()).StatusCode.Should().Be(HttpStatusCode.OK);
        var xsrf = await _client.FetchXsrfAsync(Ct); // re-fetched after sign-in: tokens are bound to the identity

        using var signOut = await _client.PostAsync("/auth/sign-out", null, xsrf, Ct);

        signOut.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var cleared = signOut.SetCookies("__Host-utp-admin").Should().ContainSingle().Subject;
        AdminIdentityTestSupport.CookieAttributes(cleared).Should().Contain(a => a.StartsWith("expires=", StringComparison.OrdinalIgnoreCase));
        using var me = await GetMeAsync();
        me.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AC_A_04_SignOutWithoutASession_Returns204_AndIsRepeatable()
    {
        var xsrf = await _client.FetchXsrfAsync(Ct);

        using var first = await _client.PostAsync("/auth/sign-out", null, xsrf, Ct);
        using var second = await _client.PostAsync("/auth/sign-out", null, xsrf, Ct);

        first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // D-1: sign-out refreshes the security stamp, so a copied cookie stops working at the next revalidation.
    private async Task<string> SignInAndCaptureCookieAsync(HttpClient client)
    {
        using var response = await client.SignInAsync(Email, AdminIdentityTestSupport.Password, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return AdminIdentityTestSupport.CookieValue(response.SetCookies("__Host-utp-admin").Should().ContainSingle().Subject);
    }

    private async Task<HttpStatusCode> GetMeWithCookieAsync(string cookie)
    {
        using var fresh = _factory.CreateSecureClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{AdminIdentityTestSupport.AdminPrefix}/me");
        request.Headers.Add("Cookie", $"__Host-utp-admin={cookie}");
        using var response = await fresh.SendAsync(request, Ct);
        return response.StatusCode;
    }

    private async Task SignOutAsync(HttpClient client)
    {
        var xsrf = await client.FetchXsrfAsync(Ct);
        using var signOut = await client.PostAsync("/auth/sign-out", null, xsrf, Ct);
        signOut.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task D_1_ACopiedSessionCookie_StopsWorkingAfterSignOut_AtTheNextRevalidation()
    {
        var id = await CreateAdminAsync();
        var copied = await SignInAndCaptureCookieAsync(_client);
        var stampBefore = (await LoadUserAsync(id)).SecurityStamp;
        (await GetMeWithCookieAsync(copied)).Should().Be(HttpStatusCode.OK, "the copied cookie is valid before sign-out");

        await SignOutAsync(_client);

        (await LoadUserAsync(id)).SecurityStamp.Should().NotBe(stampBefore, "sign-out refreshes the stamp");
        // Inside the 5 minute interval the validator is not yet due, so the cookie is still accepted (the window D-1 accepts).
        (await GetMeWithCookieAsync(copied)).Should().Be(HttpStatusCode.OK);
        _clock.Advance(TimeSpan.FromMinutes(6));
        (await GetMeWithCookieAsync(copied)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task D_1_SigningOutOneSession_EndsTheUsersOtherSessionsToo_AcceptedSideEffect()
    {
        await CreateAdminAsync();
        using var second = _factory.CreateSecureClient();
        await SignInAndCaptureCookieAsync(_client);
        var otherCookie = await SignInAndCaptureCookieAsync(second);
        (await GetMeWithCookieAsync(otherCookie)).Should().Be(HttpStatusCode.OK);

        await SignOutAsync(_client);

        _clock.Advance(TimeSpan.FromMinutes(6));
        (await GetMeWithCookieAsync(otherCookie)).Should().Be(HttpStatusCode.Unauthorized);
        (await GetMeAsync(second)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task D_1_SignOutRepeatedAfterASession_StillReturns204()
    {
        await CreateAdminAsync();
        await SignInAndCaptureCookieAsync(_client);

        await SignOutAsync(_client);
        await SignOutAsync(_client);
    }

    // ---- antiforgery (sign-in part of BR-A-04), the token endpoint ---------------------------------------------

    [Fact]
    public async Task BR_A_04_TheAntiforgeryEndpointIssuesAReadableTokenCookie_AndAnHttpOnlyCookieToken()
    {
        using var response = await _client.GetAsync($"{AdminIdentityTestSupport.AdminPrefix}/auth/antiforgery", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.CacheControl?.NoStore.Should().BeTrue();
        var readable = response.SetCookies("__Host-utp-xsrf").Should().ContainSingle().Subject;
        var readableAttributes = AdminIdentityTestSupport.CookieAttributes(readable);
        readableAttributes.Should().Contain("secure").And.Contain("samesite=strict").And.Contain("path=/");
        readableAttributes.Should().NotContain("httponly", "the SPA must read the request token");
        AdminIdentityTestSupport.CookieValue(readable).Should().NotBeNullOrEmpty();
        var companion = response.SetCookies("__Host-utp-antiforgery").Should().ContainSingle().Subject;
        AdminIdentityTestSupport.CookieAttributes(companion).Should()
            .Contain("httponly").And.Contain("secure").And.Contain("samesite=strict").And.Contain("path=/");
    }

    [Fact]
    public async Task BR_A_04_SignInWithoutTheAntiforgeryToken_Returns400AntiforgeryInvalid_AndSignsNobodyIn()
    {
        await CreateAdminAsync();
        await _client.FetchXsrfAsync(Ct); // the cookie token exists, the header is missing

        using var response = await _client.PostAsync(
            "/auth/sign-in", new { email = Email, password = AdminIdentityTestSupport.Password }, xsrf: null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
        json.GetProperty("code").GetString().Should().Be("antiforgery_invalid");
        response.SetCookies("__Host-utp-admin").Should().BeEmpty();
        (await GetMeAsync()).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BR_A_04_SignInWithAForgedToken_Returns400AntiforgeryInvalid()
    {
        await CreateAdminAsync();
        await _client.FetchXsrfAsync(Ct);

        using var response = await _client.PostAsync(
            "/auth/sign-in", new { email = Email, password = AdminIdentityTestSupport.Password }, "not-a-valid-token", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.SetCookies("__Host-utp-admin").Should().BeEmpty();
    }

    [Fact]
    public async Task BR_A_04_SignOutWithoutTheAntiforgeryToken_Returns400AntiforgeryInvalid_AndKeepsTheSession()
    {
        await CreateAdminAsync();
        (await _client.SignInAsync(Email, AdminIdentityTestSupport.Password, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var response = await _client.PostAsync("/auth/sign-out", null, xsrf: null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetMeAsync()).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task BR_A_04_ATokenFetchedBeforeSignInIsRejectedAfterSignIn_AndAFreshTokenWorks()
    {
        await CreateAdminAsync();
        var beforeSignIn = await _client.FetchXsrfAsync(Ct);
        (await _client.SignInAsync(Email, AdminIdentityTestSupport.Password, Ct, beforeSignIn)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var stale = await _client.PostAsync("/auth/sign-out", null, beforeSignIn, Ct);
        var fresh = await _client.FetchXsrfAsync(Ct);
        using var retried = await _client.PostAsync("/auth/sign-out", null, fresh, Ct);

        stale.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        retried.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ---- 401 and 403 instead of redirects ----------------------------------------------------------------------

    [Fact]
    public async Task AC_A1_WithTheRealCookieScheme_AnAnonymousAdminRequestReturns401ProblemDetails_NotARedirect()
    {
        using var response = await GetMeAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.Location.Should().BeNull();
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task AC_A2_WithTheRealCookieScheme_ASignedInUserWithoutTheAdminRoleGets403ProblemDetails_NotARedirect()
    {
        var id = await AdminIdentityTestSupport.CreateUserAsync(_factory.Services, "customer@example.test", admin: false);
        var cookie = await ForgeSessionCookieAsync(id);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{AdminIdentityTestSupport.AdminPrefix}/me");
        request.Headers.Add("Cookie", $"__Host-utp-admin={cookie}");
        using var response = await _client.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Headers.Location.Should().BeNull();
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task AC_A4_PublicEndpointsStayAnonymousWithTheRealCookieScheme()
    {
        using var response = await _client.GetAsync("/api/v1/health", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---- AC-A-06, BR-A-02: idle and absolute timeouts, AC-A8 / ADR-0010: security stamp -------------------------

    [Fact]
    public async Task AC_A_06_After30MinutesWithoutARequest_TheNextAdminCallReturns401()
    {
        await CreateAdminAsync();
        (await _client.SignInAsync(Email, AdminIdentityTestSupport.Password, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        _clock.Advance(TimeSpan.FromMinutes(31));

        (await GetMeAsync()).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BR_A_02_ActivityKeepsTheSessionAlive_ASlidingWindowOf30Minutes()
    {
        await CreateAdminAsync();
        (await _client.SignInAsync(Email, AdminIdentityTestSupport.Password, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        for (var request = 1; request <= 3; request++)
        {
            _clock.Advance(TimeSpan.FromMinutes(20));
            (await GetMeAsync()).StatusCode.Should().Be(HttpStatusCode.OK, $"request {request}, 20 minutes after the last one");
        }

        _clock.Advance(TimeSpan.FromMinutes(31));
        (await GetMeAsync()).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BR_A_02_TheSessionEndsAfter8HoursEvenWithContinuousActivity()
    {
        await CreateAdminAsync();
        (await _client.SignInAsync(Email, AdminIdentityTestSupport.Password, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        for (var minutes = 20; minutes <= 460; minutes += 20)
        {
            _clock.Advance(TimeSpan.FromMinutes(20));
            (await GetMeAsync()).StatusCode.Should().Be(HttpStatusCode.OK, $"{minutes} minutes after sign-in");
        }

        _clock.Advance(TimeSpan.FromMinutes(20)); // 8 hours after sign-in
        (await GetMeAsync()).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AC_A8_ChangingThePassword_RefreshesTheSecurityStamp_AndEndsTheSessionWithinTheValidationInterval()
    {
        var id = await CreateAdminAsync();
        (await _client.SignInAsync(Email, AdminIdentityTestSupport.Password, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        var stampBefore = (await LoadUserAsync(id)).SecurityStamp;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
            var user = await users.FindByIdAsync(id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            (await users.ChangePasswordAsync(user!, AdminIdentityTestSupport.Password, "Another-Passw0rd-42")).Succeeded.Should().BeTrue();
        }

        (await LoadUserAsync(id)).SecurityStamp.Should().NotBe(stampBefore);
        _clock.Advance(TimeSpan.FromMinutes(6)); // past the 5 minute security-stamp validation interval
        (await GetMeAsync()).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AC_A8_TheSecurityStampIsOnlyRevalidatedEveryFiveMinutes()
    {
        _factory.Services.GetRequiredService<IOptions<SecurityStampValidatorOptions>>().Value.ValidationInterval
            .Should().Be(TimeSpan.FromMinutes(5));
        await Task.CompletedTask;
    }

    // M-1: the locked-out path spends the same hashing cost as the unknown-user path (no timing oracle).
    [Fact]
    public async Task M_1_ALockedOutSignIn_HashesThePasswordTheSameNumberOfTimesAsAnUnknownUser()
    {
        await CreateAdminAsync();
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var wrong = await _client.SignInAsync(Email, "Wrong-Password-1234", Ct);
            wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var xsrf = await _client.FetchXsrfAsync(Ct);

        _hasher.Reset();
        using var unknown = await _client.SignInAsync("nobody.test", AdminIdentityTestSupport.Password, Ct, xsrf);
        var unknownCalls = _hasher.Calls;

        _hasher.Reset();
        using var locked = await _client.SignInAsync(Email, AdminIdentityTestSupport.Password, Ct, xsrf);
        var lockedCalls = _hasher.Calls;

        unknown.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        locked.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        unknownCalls.Should().BeGreaterThan(0);
        lockedCalls.Should().Be(unknownCalls);
    }

    // L-2: the anonymous auth endpoints declare only the statuses they can return.
    [Fact]
    public async Task L_2_TheAnonymousAuthEndpoints_DeclareExactlyTheStatusesTheyCanReturn()
    {
        var document = JsonDocument.Parse(await _client.GetStringAsync("/openapi/v1.json", Ct)).RootElement;
        var paths = document.GetProperty("paths");

        IReadOnlyList<string> Statuses(string path, string method) =>
            [.. paths.GetProperty(path).GetProperty(method).GetProperty("responses").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)];

        Statuses("/api/v1/admin/auth/antiforgery", "get").Should().Equal("204");
        Statuses("/api/v1/admin/auth/sign-in", "post").Should().Equal("200", "400", "401", "429");
        Statuses("/api/v1/admin/auth/sign-out", "post").Should().Equal("204", "400");

        var badRequest = paths.GetProperty("/api/v1/admin/auth/sign-in").GetProperty("post")
            .GetProperty("responses").GetProperty("400").GetProperty("content").GetProperty("application/problem+json")
            .GetProperty("schema").GetProperty("$ref").GetString();
        badRequest.Should().EndWith("/HttpValidationProblemDetails");
        // The protected admin endpoint keeps the inherited 401 and 403.
        Statuses("/api/v1/admin/me", "get").Should().Contain(["200", "401", "403"]);
    }

    // L-5: a deactivation that did not refresh the security stamp still ends the session at the next revalidation.
    [Fact]
    public async Task L_5_ADeactivatedAdminWithAnUnchangedSecurityStamp_IsSignedOutAtTheNextRevalidation()
    {
        var id = await CreateAdminAsync();
        (await _client.SignInAsync(Email, AdminIdentityTestSupport.Password, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        var stampBefore = (await LoadUserAsync(id)).SecurityStamp;

        await using (var db = _database.CreateContext())
        {
            (await db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false), Ct)).Should().Be(1);
        }

        (await LoadUserAsync(id)).SecurityStamp.Should().Be(stampBefore, "the stamp was deliberately not refreshed");
        _clock.Advance(TimeSpan.FromMinutes(6)); // past the 5 minute validation interval, inside the 30 minute idle window
        (await GetMeAsync()).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- sign-in rate limit (BR-A-01) --------------------------------------------------------------------------

    [Fact]
    public async Task BR_A_01_SignInIsRateLimitedPerIp_ByAuthPerMinute_With429()
    {
        using var client = _limitedFactory.CreateSecureClient();
        var xsrf = await client.FetchXsrfAsync(Ct);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            using var allowed = await client.SignInAsync("nobody@example.test", "Whatever-Passw0rd", Ct, xsrf);
            allowed.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"attempt {attempt}");
        }

        using var limited = await client.SignInAsync("nobody@example.test", "Whatever-Passw0rd", Ct, xsrf);

        limited.StatusCode.Should().Be((HttpStatusCode)429);
        limited.Headers.RetryAfter.Should().NotBeNull();
        limited.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        limited.SetCookies("__Host-utp-admin").Should().BeEmpty();
    }

    [Fact]
    public async Task BR_A_01_FetchingAntiforgeryTokensDoesNotUseUpSignInAttempts()
    {
        using var client = _limitedFactory.CreateSecureClient();

        for (var fetch = 1; fetch <= 10; fetch++)
        {
            (await client.FetchXsrfAsync(Ct)).Should().NotBeNullOrEmpty();
        }

        using var signIn = await client.SignInAsync("nobody@example.test", "Whatever-Passw0rd", Ct);
        signIn.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- audit events and log hygiene (BR-A-31, AC-A-40) ------------------------------------------------------

    [Fact]
    public async Task BR_A_31_SignInSuccessAndSignOutAreLogged_WithUserIdAndNoSecrets()
    {
        var id = await CreateAdminAsync();
        (await _client.SignInAsync(Email, AdminIdentityTestSupport.Password, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        var xsrf = await _client.FetchXsrfAsync(Ct);
        (await _client.PostAsync("/auth/sign-out", null, xsrf, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var events = _factory.Logs.Entries.Where(e => e.State.ContainsKey("Action")).ToList();
        var signIn = events.Should().ContainSingle(e => (string?)e.State["Action"] == "sign_in").Subject;
        signIn.State["UserId"].Should().Be(id);
        signIn.State["TraceId"].Should().NotBeNull();
        signIn.State.Should().ContainKey("ClientIp");
        var idText = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        signIn.State["EntityType"].Should().Be("AdminUser");
        signIn.State["EntityId"].Should().Be(idText);
        var signOutEvent = events.Should().ContainSingle(e => (string?)e.State["Action"] == "sign_out" && Equals(e.State["UserId"], id)).Subject;
        signOutEvent.State["EntityType"].Should().Be("AdminUser");
        signOutEvent.State["EntityId"].Should().Be(idText);
        signOutEvent.State["TraceId"].Should().NotBeNull();
        signOutEvent.State.Should().ContainKey("ClientIp");
        signIn.State.Keys.Concat(signOutEvent.State.Keys).Should().NotContain(k => k.Contains("Email", StringComparison.OrdinalIgnoreCase));
        AllLogText().Should().NotContain(AdminIdentityTestSupport.Password);
    }

    [Fact]
    public async Task AC_A_40_AFailedSignIn_LogsNoPassword_AndNoEmailForAnUnknownAccount()
    {
        await CreateAdminAsync();
        const string unknownEmail = "nobody.at.all@example.test";
        const string typedPassword = "Typed-Passw0rd-Marker-77";

        using var unknown = await _client.SignInAsync(unknownEmail, typedPassword, Ct);
        using var wrong = await _client.SignInAsync(Email, typedPassword, Ct);

        unknown.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var failures = _factory.Logs.Entries.Where(e => (string?)e.State.GetValueOrDefault("Action") == "sign_in_failed").ToList();
        failures.Should().HaveCount(2);
        var text = AllLogText();
        text.Should().NotContain(typedPassword);
        text.Should().NotContain(unknownEmail);
        text.Should().NotContain(Email, "failed sign-in events carry the user id, not the address");
        failures.Select(f => (string?)f.State["Reason"]).Should().BeEquivalentTo("unknown_user", "bad_password");
    }

    [Fact]
    public async Task BR_A_31_ALockoutIsLogged()
    {
        var id = await CreateAdminAsync();
        for (var attempt = 1; attempt <= 6; attempt++)
        {
            using var response = await _client.SignInAsync(Email, "Wrong-Password-1234", Ct);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        _factory.Logs.Entries.Should().Contain(e =>
            (string?)e.State.GetValueOrDefault("Action") == "sign_in_locked_out" && Equals(e.State["UserId"], id));
        AllLogText().Should().NotContain("Wrong-Password-1234");
    }

    private sealed class CountingPasswordHasher : IPasswordHasher<AdminUser>
    {
        private readonly PasswordHasher<AdminUser> _inner = new();
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public void Reset() => Interlocked.Exchange(ref _calls, 0);

        public string HashPassword(AdminUser user, string password)
        {
            Interlocked.Increment(ref _calls);
            return _inner.HashPassword(user, password);
        }

        public PasswordVerificationResult VerifyHashedPassword(AdminUser user, string hashedPassword, string providedPassword)
        {
            Interlocked.Increment(ref _calls);
            return _inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
        }
    }

    private string AllLogText() =>
        string.Join(
            "\n",
            _factory.Logs.Entries.Select(e => e.Message + " " + string.Join(" ", e.State.Select(kv => $"{kv.Key}={kv.Value}"))));

    /// <summary>Builds a valid session cookie for a user the sign-in endpoint would refuse (no admin role).</summary>
    private async Task<string> ForgeSessionCookieAsync(int userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
        var user = await users.FindByIdAsync(userId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var principal = await scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<AdminUser>>().CreateAsync(user!);
        var now = _clock.GetUtcNow();
        var properties = AdminIdentitySetup.NewSessionProperties(_clock);
        properties.IssuedUtc = now;
        properties.ExpiresUtc = now + AdminIdentitySetup.IdleTimeout;
        var options = _factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        return options.TicketDataFormat.Protect(new AuthenticationTicket(principal, properties, IdentityConstants.ApplicationScheme));
    }
}
