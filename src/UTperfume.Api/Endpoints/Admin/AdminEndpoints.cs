using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace UTperfume.Api.Endpoints.Admin;

internal static class AdminPolicy
{
    public const string Name = "AdminOnly";
    public const string Role = UTperfume.Infrastructure.Identity.AdminRoles.CatalogAdmin;

    public static AuthorizationBuilder AddAdminPolicy(this AuthorizationBuilder builder) =>
        builder.AddPolicy(Name, policy => policy.RequireAuthenticatedUser().RequireRole(Role));
}

/// <summary>Current admin (0002 §6.2). Carries no secrets; the id is the identity subject, not a database key contract.</summary>
public sealed record AdminMe(string Id, string Email, string DisplayName, IReadOnlyList<string> Roles)
{
    internal static AdminMe From(ClaimsPrincipal user) => new(
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
        user.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
        user.FindFirstValue(UTperfume.Api.Infrastructure.AdminClaimTypes.DisplayName) ?? user.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
        user.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList());
}

/// <summary>
/// Lets a host add routes to the admin group. Only the test host registers one (P4-08); production admin endpoints
/// are mapped explicitly in <see cref="AdminEndpoints.MapAdminEndpoints"/>.
/// </summary>
internal interface IAdminRouteContributor
{
    void Map(RouteGroupBuilder admin);
}

internal static class AdminEndpoints
{
    public const string RoutePrefix = "/api/v1/admin";

    /// <summary>
    /// The admin group that every admin endpoint registers on. It requires the <c>AdminOnly</c> policy (role
    /// <c>Catalog.Admin</c>; 401 anonymous, 403 without the role), is excluded from the output cache and the public rate
    /// limit, and its responses are <c>no-store</c> through <c>UseApiCacheControl</c> (BR-A-22). Only the BR-A-05
    /// <c>auth/*</c> endpoints may call <c>AllowAnonymous()</c>; the boundary test enforces this. Later tasks add one
    /// registration line here.
    /// </summary>
    public static RouteGroupBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup(RoutePrefix)
            .RequireAuthorization(AdminPolicy.Name)
            .WithTags("Admin")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        admin.MapGet("/me", (ClaimsPrincipal user) => TypedResults.Ok(AdminMe.From(user)))
            .WithName("GetAdminMe")
            .Produces<AdminMe>();

        admin.MapAdminAuthEndpoints();

        foreach (var contributor in app.ServiceProvider.GetServices<IAdminRouteContributor>())
        {
            contributor.Map(admin);
        }

        return admin;
    }
}
