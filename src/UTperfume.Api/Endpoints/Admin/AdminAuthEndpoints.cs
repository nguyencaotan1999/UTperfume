using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using UTperfume.Api.Infrastructure;
using UTperfume.Infrastructure.Identity;

namespace UTperfume.Api.Endpoints.Admin;

/// <summary>Body of <c>POST /admin/auth/sign-in</c> (0002 section 6.2).</summary>
public sealed record SignInRequest(string? Email, string? Password);

/// <summary>
/// The anonymous <c>auth/*</c> endpoints of BR-A-05 that P4-02 delivers: antiforgery token, sign-in and sign-out.
/// (The password-reset endpoints are P4-05.) Sign-in and sign-out validate the antiforgery token with the shared
/// filter; P4-03 applies the same filter to every other admin unsafe method.
/// </summary>
internal static class AdminAuthEndpoints
{
    private const int MaxEmailLength = 256;
    private const int MaxPasswordLength = 256;

    public static void MapAdminAuthEndpoints(this RouteGroupBuilder admin)
    {
        var auth = admin.MapGroup("/auth").WithTags("Admin auth");

        auth.MapGet("/antiforgery", IssueAntiforgery)
            .AllowAnonymous()
            .WithoutInheritedAuthResponses()
            .WithName("GetAdminAntiforgery")
            .Produces(StatusCodes.Status204NoContent);

        auth.MapPost("/sign-in", SignInAsync)
            .AllowAnonymous()
            .RequireRateLimiting(AdminIdentitySetup.SignInRateLimitPolicy)
            .WithoutInheritedAuthResponses()
            .WithName("AdminSignIn")
            .Produces<AdminMe>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        auth.MapPost("/sign-out", SignOutAsync)
            .AllowAnonymous()
            .WithoutInheritedAuthResponses()
            .WithName("AdminSignOut")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// 204. Stores the antiforgery cookie token (HttpOnly) and hands the request token to the SPA in the readable
    /// <c>__Host-utp-xsrf</c> cookie. Tokens are bound to the signed-in identity, so the SPA calls this again after
    /// sign-in and after sign-out (BR-A-04).
    /// </summary>
    private static IResult IssueAntiforgery(HttpContext context, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        context.Response.Cookies.Append(
            AdminIdentitySetup.XsrfCookieName,
            tokens.RequestToken ?? string.Empty,
            new CookieOptions
            {
                HttpOnly = false,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/",
                IsEssential = true,
            });
        return TypedResults.NoContent();
    }

    /// <summary>
    /// BR-A-01. Unknown email, wrong password, locked account, deactivated account and a user without the admin role all
    /// return the same 401. Lockout counts failures (<c>lockoutOnFailure: true</c>). The failure log never carries the
    /// email address or the password.
    /// </summary>
    private static async Task<IResult> SignInAsync(
        SignInRequest request,
        UserManager<AdminUser> users,
        SignInManager<AdminUser> signIn,
        IPasswordHasher<AdminUser> hasher,
        TimeProvider time,
        AdminAuthAudit audit)
    {
        var email = request.Email?.Trim();
        var password = request.Password;
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(email))
        {
            errors["email"] = ["Email is required."];
        }
        else if (email.Length > MaxEmailLength)
        {
            errors["email"] = [$"Email must be at most {MaxEmailLength} characters."];
        }

        if (string.IsNullOrEmpty(password))
        {
            errors["password"] = ["Password is required."];
        }
        else if (password.Length > MaxPasswordLength)
        {
            errors["password"] = [$"Password must be at most {MaxPasswordLength} characters."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var user = await users.FindByEmailAsync(email!);
        if (user is null || !user.IsActive)
        {
            // Spend the same hashing time as a real check so the response time does not reveal the account state.
            SpendHashingCost(hasher, password!);
            audit.SignInFailed(user?.Id, user is null ? "unknown_user" : "inactive");
            return InvalidCredentials();
        }

        var result = await signIn.CheckPasswordSignInAsync(user, password!, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            // CheckPasswordSignInAsync returns LockedOut without hashing; spend the same cost as the other failure paths.
            SpendHashingCost(hasher, password!);
            audit.LockedOut(user.Id);
            return InvalidCredentials();
        }

        if (!result.Succeeded)
        {
            audit.SignInFailed(user.Id, "bad_password");
            return InvalidCredentials();
        }

        if (!await users.IsInRoleAsync(user, AdminRoles.CatalogAdmin))
        {
            audit.SignInFailed(user.Id, "not_admin");
            return InvalidCredentials();
        }

        await signIn.SignInAsync(user, AdminIdentitySetup.NewSessionProperties(time), authenticationMethod: "pwd");
        audit.SignedIn(user.Id);

        var roles = await users.GetRolesAsync(user);
        return TypedResults.Ok(new AdminMe(
            user.Id.ToString(CultureInfo.InvariantCulture),
            user.Email ?? string.Empty,
            user.DisplayName,
            [.. roles.Order(StringComparer.Ordinal)]));
    }

    /// <summary>
    /// BR-A-03, decision D-1. With a session: refreshes the user's security stamp first, so a copied cookie stops working
    /// at the next revalidation (within 5 minutes; this also ends the user's other sessions), then clears the cookie.
    /// Returns 204 with or without a session (idempotent). A failed stamp update is logged as a warning (user id only) and
    /// the cookie is cleared anyway.
    /// </summary>
    private static async Task<IResult> SignOutAsync(HttpContext http, UserManager<AdminUser> users, AdminAuthAudit audit)
    {
        var userId = int.TryParse(
            http.User.FindFirstValue(ClaimTypes.NameIdentifier), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : (int?)null;

        if (userId is { } signedInId)
        {
            try
            {
                var user = await users.FindByIdAsync(signedInId.ToString(CultureInfo.InvariantCulture));
                if (user is not null)
                {
                    var result = await users.UpdateSecurityStampAsync(user);
                    if (!result.Succeeded)
                    {
                        audit.StampRefreshFailed(signedInId, string.Join(",", result.Errors.Select(e => e.Code)));
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Never leak the exception to the client and never keep the session because of it.
                audit.StampRefreshFailed(signedInId, ex.GetType().Name);
            }
        }

        // SignInManager.SignOutAsync also signs out the external and two-factor schemes, which are not registered.
        await http.SignOutAsync(IdentityConstants.ApplicationScheme);
        audit.SignedOut(userId);
        return TypedResults.NoContent();
    }

    private static void SpendHashingCost(IPasswordHasher<AdminUser> hasher, string password) =>
        _ = hasher.HashPassword(new AdminUser(), password);

    private static IResult InvalidCredentials() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Unauthorized",
            detail: "The email or password is incorrect.");
}

/// <summary>
/// Structured audit events for sign-in success, sign-in failure, lockout and sign-out (BR-A-31): action, entity, user id,
/// client IP and trace id. The inputs are ids and fixed reason codes only, so no password, cookie, token or email
/// address can reach the log.
/// </summary>
internal sealed partial class AdminAuthAudit(IHttpContextAccessor accessor, ILogger<AdminAuthAudit> logger)
{
    public void SignedIn(int userId) => Success(logger, "sign_in", EntityType, Id(userId), userId, ClientIp, TraceId);

    public void SignedOut(int? userId) => SignOut(logger, "sign_out", EntityType, Id(userId), userId, ClientIp, TraceId);

    public void SignInFailed(int? userId, string reason) =>
        Failed(logger, "sign_in_failed", reason, EntityType, Id(userId), userId, ClientIp, TraceId);

    public void LockedOut(int userId) =>
        Failed(logger, "sign_in_locked_out", "locked_out", EntityType, Id(userId), userId, ClientIp, TraceId);

    public void StampRefreshFailed(int userId, string reason) =>
        StampFailed(logger, "sign_out_stamp_refresh_failed", reason, EntityType, Id(userId), userId, ClientIp, TraceId);

    private const string EntityType = "AdminUser";

    private static string? Id(int? userId) => userId?.ToString(CultureInfo.InvariantCulture);

    private string? ClientIp => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    private string TraceId =>
        System.Diagnostics.Activity.Current?.Id ?? accessor.HttpContext?.TraceIdentifier ?? string.Empty;

    [LoggerMessage(EventId = 5100, Level = LogLevel.Information,
        Message = "Admin audit: {Action} {EntityType} {EntityId} by user {UserId} from {ClientIp} (trace {TraceId})")]
    private static partial void Success(
        ILogger logger, string action, string entityType, string? entityId, int userId, string? clientIp, string traceId);

    [LoggerMessage(EventId = 5101, Level = LogLevel.Warning,
        Message = "Admin audit: {Action} ({Reason}) {EntityType} {EntityId} by user {UserId} from {ClientIp} (trace {TraceId})")]
    private static partial void Failed(
        ILogger logger, string action, string reason, string entityType, string? entityId, int? userId, string? clientIp, string traceId);

    [LoggerMessage(EventId = 5103, Level = LogLevel.Warning,
        Message = "Admin audit: {Action} ({Reason}) {EntityType} {EntityId} by user {UserId} from {ClientIp} (trace {TraceId})")]
    private static partial void StampFailed(
        ILogger logger, string action, string reason, string entityType, string? entityId, int userId, string? clientIp, string traceId);

    [LoggerMessage(EventId = 5102, Level = LogLevel.Information,
        Message = "Admin audit: {Action} {EntityType} {EntityId} by user {UserId} from {ClientIp} (trace {TraceId})")]
    private static partial void SignOut(
        ILogger logger, string action, string entityType, string? entityId, int? userId, string? clientIp, string traceId);
}

/// <summary>Removes the 401 and 403 responses the admin group declares; the anonymous auth endpoints cannot return them (L-2).</summary>
internal static class AdminAuthEndpointMetadata
{
    public static TBuilder WithoutInheritedAuthResponses<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.Add(endpoint =>
        {
            for (var i = endpoint.Metadata.Count - 1; i >= 0; i--)
            {
                if (endpoint.Metadata[i] is Microsoft.AspNetCore.Http.Metadata.IProducesResponseTypeMetadata
                    {
                        StatusCode: StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden,
                    })
                {
                    endpoint.Metadata.RemoveAt(i);
                }
            }
        });
        return builder;
    }
}
