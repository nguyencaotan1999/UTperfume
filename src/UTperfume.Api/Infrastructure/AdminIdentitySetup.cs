using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using UTperfume.Infrastructure.Identity;
using UTperfume.Infrastructure.Persistence;

namespace UTperfume.Api.Infrastructure;

/// <summary>Claim types the admin area adds on top of the standard Identity claims.</summary>
internal static class AdminClaimTypes
{
    public const string DisplayName = "utp:display_name";
}

/// <summary>
/// ASP.NET Core Identity with a same-origin cookie session for the admin area (ADR-0010, P4-02): password policy,
/// lockout, the <c>__Host-utp-admin</c> cookie, 401 and 403 instead of redirects, security-stamp revalidation every
/// 5 minutes, antiforgery cookie and header names, and the sign-in rate limit. <c>MapIdentityApi</c> is not used.
/// </summary>
internal static class AdminIdentitySetup
{
    public const string SessionCookieName = "__Host-utp-admin";
    public const string AntiforgeryCookieName = "__Host-utp-antiforgery";
    public const string XsrfCookieName = "__Host-utp-xsrf";
    public const string XsrfHeaderName = "X-XSRF-TOKEN";
    public const string SignInRateLimitPolicy = "admin-sign-in";

    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan AbsoluteTimeout = TimeSpan.FromHours(8);
    public static readonly TimeSpan SecurityStampInterval = TimeSpan.FromMinutes(5);

    /// <summary>Authentication property that carries the absolute (8 hour) end of the session across sliding renewals.</summary>
    internal const string AbsoluteExpiryKey = "utp.absolute_expiry";

    public static IServiceCollection AddAdminIdentity(this IServiceCollection services)
    {
        services.AddIdentityCore<AdminUser>(o =>
            {
                // Password policy (0002 / architecture 7.1): length matters more than symbol rules.
                o.Password.RequiredLength = 12;
                o.Password.RequireDigit = true;
                o.Password.RequireUppercase = true;
                o.Password.RequireLowercase = true;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequiredUniqueChars = 4;

                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                o.Lockout.AllowedForNewUsers = true;

                // No self-registration; admins are created by the bootstrap command or by other admins.
                o.User.RequireUniqueEmail = true;
                o.SignIn.RequireConfirmedAccount = false;
            })
            .AddRoles<IdentityRole<int>>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager<AdminSignInManager>()
            .AddDefaultTokenProviders()
            .AddClaimsPrincipalFactory<AdminClaimsPrincipalFactory>();

        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = SecurityStampInterval);

        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddCookie(IdentityConstants.ApplicationScheme, o =>
            {
                o.Cookie.Name = SessionCookieName;
                o.Cookie.HttpOnly = true;
                o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                o.Cookie.SameSite = SameSiteMode.Strict;
                o.Cookie.Path = "/";
                o.Cookie.IsEssential = true;
                o.ExpireTimeSpan = IdleTimeout;
                o.SlidingExpiration = true;

                o.Events.OnRedirectToLogin = static context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                o.Events.OnRedirectToAccessDenied = static context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
                o.Events.OnValidatePrincipal = ValidateSessionAsync;
            });
        // Never issued: no "remember this browser" and no enforced two-factor (Q-14). The security-stamp validator signs
        // this scheme out directly when a stamp no longer matches, so a handler must exist or that sign-out would throw.
        services.AddAuthentication()
            .AddCookie(IdentityConstants.TwoFactorRememberMeScheme, o =>
            {
                o.Cookie.Name = "__Host-utp-admin-2fa";
                o.Cookie.HttpOnly = true;
                o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                o.Cookie.SameSite = SameSiteMode.Strict;
                o.Cookie.Path = "/";
            });
        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<TimeProvider>((options, time) => options.TimeProvider = time);

        services.AddAntiforgery(o =>
        {
            o.HeaderName = XsrfHeaderName;
            // The antiforgery cookie token is HttpOnly. The request token is handed to the SPA in the readable
            // __Host-utp-xsrf cookie by GET /auth/antiforgery.
            o.Cookie.Name = AntiforgeryCookieName;
            o.Cookie.HttpOnly = true;
            o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.Path = "/";
        });

        // Sign-in is limited per client IP (AuthPerMinute). A separate policy and bucket from the public limit, so
        // fetching antiforgery tokens never consumes sign-in attempts.
        services.AddRateLimiter(o => o.AddPolicy(SignInRateLimitPolicy, context =>
        {
            var limit = context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value.AuthPerMinute;
            return RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
        }));

        return services;
    }

    /// <summary>The absolute-limit check, then the security-stamp and active-user check (every 5 minutes).</summary>
    private static async Task ValidateSessionAsync(CookieValidatePrincipalContext context)
    {
        var time = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>();
        var hasLimit = context.Properties.Items.TryGetValue(AbsoluteExpiryKey, out var raw);
        if (!hasLimit
            || !DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var limit)
            || time.GetUtcNow() >= limit)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return;
        }

        await SecurityStampValidator.ValidatePrincipalAsync(context);
    }

    /// <summary>Properties for a new session: not persistent, with the 8 hour absolute end.</summary>
    public static AuthenticationProperties NewSessionProperties(TimeProvider time) =>
        new()
        {
            IsPersistent = false,
            AllowRefresh = true,
            Items = { [AbsoluteExpiryKey] = (time.GetUtcNow() + AbsoluteTimeout).ToString("O", CultureInfo.InvariantCulture) },
        };
}

/// <summary>Adds the display name to the principal so <c>GET /admin/me</c> can return it without a database call.</summary>
internal sealed class AdminClaimsPrincipalFactory(
    UserManager<AdminUser> userManager,
    RoleManager<IdentityRole<int>> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<AdminUser, IdentityRole<int>>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AdminUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(AdminClaimTypes.DisplayName, user.DisplayName));
        return identity;
    }
}

/// <summary>
/// Signs out only the admin session cookie. The stock <c>SignInManager.SignOutAsync</c> also signs out the external and
/// two-factor schemes, which the admin area does not register (no external login, no enforced two-factor, Q-14), so it
/// would throw. The security-stamp validator calls it when a stamp no longer matches.
/// </summary>
internal sealed class AdminSignInManager(
    UserManager<AdminUser> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<AdminUser> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<AdminUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<AdminUser> confirmation)
    : SignInManager<AdminUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    public override Task SignOutAsync() => Context.SignOutAsync(IdentityConstants.ApplicationScheme);

    /// <summary>
    /// The security-stamp validator (every 5 minutes) calls this. Besides the stamp, the user must still be active
    /// (L-5, defence in depth): a deactivation path that forgets to refresh the stamp still ends the session.
    /// </summary>
    public override async Task<AdminUser?> ValidateSecurityStampAsync(ClaimsPrincipal? principal)
    {
        var user = await base.ValidateSecurityStampAsync(principal);
        return user is { IsActive: true } ? user : null;
    }
}
