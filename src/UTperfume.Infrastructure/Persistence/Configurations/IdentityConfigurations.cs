using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UTperfume.Infrastructure.Identity;

namespace UTperfume.Infrastructure.Persistence.Configurations;

/// <summary>The Identity tables in schema <c>identity</c> (database model 3.13). Applied by <see cref="AppDbContext"/> after the Identity base model.</summary>
internal static class IdentityConfigurations
{
    public const string Schema = "identity";

    public static void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AdminUser>(b =>
        {
            b.ToTable("Users", Schema);
            b.Property(u => u.DisplayName).HasMaxLength(AdminUser.DisplayNameMaxLength).IsRequired();
            b.Property(u => u.IsActive).HasDefaultValue(true);
            b.Property(u => u.CreatedAtUtc).HasColumnType("datetime2(3)");
        });

        modelBuilder.Entity<IdentityRole<int>>(b =>
        {
            b.ToTable("Roles", Schema);
            SeedRole(b);
        });

        modelBuilder.Entity<IdentityUserRole<int>>().ToTable("UserRoles", Schema);
        modelBuilder.Entity<IdentityUserClaim<int>>().ToTable("UserClaims", Schema);
        modelBuilder.Entity<IdentityUserLogin<int>>().ToTable("UserLogins", Schema);
        modelBuilder.Entity<IdentityUserToken<int>>().ToTable("UserTokens", Schema);
        modelBuilder.Entity<IdentityRoleClaim<int>>().ToTable("RoleClaims", Schema);
    }

    private static void SeedRole(EntityTypeBuilder<IdentityRole<int>> builder) =>
        builder.HasData(new IdentityRole<int>
        {
            Id = AdminRoles.CatalogAdminId,
            Name = AdminRoles.CatalogAdmin,
            NormalizedName = AdminRoles.CatalogAdmin.ToUpperInvariant(),
            ConcurrencyStamp = AdminRoles.CatalogAdminConcurrencyStamp,
        });
}
