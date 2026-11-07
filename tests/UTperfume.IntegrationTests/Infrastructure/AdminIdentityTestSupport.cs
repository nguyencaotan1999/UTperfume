using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using UTperfume.Infrastructure.Identity;

namespace UTperfume.IntegrationTests.Infrastructure;

/// <summary>A clock the tests can move forward. It starts at the real current time.</summary>
public sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>Helpers for tests that use the real Identity cookie scheme instead of the test scheme (P4-02).</summary>
internal static class AdminIdentityTestSupport
{
    public const string Password = "Correct-Horse-9-Battery";
    public const string AdminPrefix = "/api/v1/admin";

    /// <summary>Removes every Identity user so each test starts without accounts. The seeded role stays.</summary>
    public static Task ResetIdentityAsync(TestDatabase database) =>
        database.ResetAsync(
            ["identity.UserRoles", "identity.UserTokens", "identity.UserLogins", "identity.UserClaims", "identity.Users"]);

    public static async Task<int> CreateUserAsync(
        IServiceProvider services,
        string email,
        string password = Password,
        bool admin = true,
        bool active = true,
        string displayName = "Test Admin")
    {
        await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
        var user = new AdminUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = displayName,
            IsActive = active,
            CreatedAtUtc = DateTime.UtcNow,
        };
        var created = await users.CreateAsync(user, password);
        created.Succeeded.Should().BeTrue(string.Join("; ", created.Errors.Select(e => e.Description)));
        if (admin)
        {
            (await users.AddToRoleAsync(user, AdminRoles.CatalogAdmin)).Succeeded.Should().BeTrue();
        }

        return user.Id;
    }

    /// <summary>A client with a cookie jar and an https base address, because the cookies are <c>Secure</c>.</summary>
    public static HttpClient CreateSecureClient(this ApiFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    /// <summary>The Set-Cookie header lines of a response for the named cookie.</summary>
    public static IReadOnlyList<string> SetCookies(this HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? [.. values.Where(v => v.StartsWith(name + "=", StringComparison.Ordinal))]
            : [];

    public static string CookieValue(string setCookie) =>
        Uri.UnescapeDataString(setCookie[(setCookie.IndexOf('=', StringComparison.Ordinal) + 1)..].Split(';')[0]);

    public static HashSet<string> CookieAttributes(string setCookie) =>
        new(setCookie.Split(';', StringSplitOptions.TrimEntries).Skip(1), StringComparer.OrdinalIgnoreCase);

    /// <summary>GET /auth/antiforgery and return the request token from the readable cookie.</summary>
    public static async Task<string> FetchXsrfAsync(this HttpClient client, CancellationToken ct)
    {
        using var response = await client.GetAsync($"{AdminPrefix}/auth/antiforgery", ct);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);
        var cookie = response.SetCookies("__Host-utp-xsrf").Should().ContainSingle().Subject;
        return CookieValue(cookie);
    }

    public static Task<HttpResponseMessage> PostAsync(
        this HttpClient client, string route, object? body, string? xsrf, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, AdminPrefix + route);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (xsrf is not null)
        {
            request.Headers.Add("X-XSRF-TOKEN", xsrf);
        }

        return client.SendAsync(request, ct);
    }

    public static async Task<HttpResponseMessage> SignInAsync(
        this HttpClient client, string email, string password, CancellationToken ct, string? xsrf = null)
    {
        xsrf ??= await client.FetchXsrfAsync(ct);
        return await client.PostAsync("/auth/sign-in", new { email, password }, xsrf, ct);
    }
}
