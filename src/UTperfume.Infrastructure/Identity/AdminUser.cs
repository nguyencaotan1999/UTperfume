using Microsoft.AspNetCore.Identity;

namespace UTperfume.Infrastructure.Identity;

/// <summary>
/// An admin account (ADR-0010, database model 3.13). Keys are <c>int</c>. An inactive user cannot sign in; the check is
/// enforced in the sign-in endpoint. Accounts are never hard-deleted by the application.
/// </summary>
public class AdminUser : IdentityUser<int>
{
    public const int DisplayNameMaxLength = 100;

    public string DisplayName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Names and fixed seed values of the single admin role (D-A1).</summary>
public static class AdminRoles
{
    /// <summary>The one role of the admin area. Holders can perform every admin function.</summary>
    public const string CatalogAdmin = "Catalog.Admin";

    /// <summary>Fixed id and stamp so the <c>HasData</c> seed never produces a spurious UPDATE in a later migration.</summary>
    internal const int CatalogAdminId = 1;

    internal const string CatalogAdminConcurrencyStamp = "5f0c4f0e-6b52-4c3e-9a33-8f2b7a1d9c01";
}
