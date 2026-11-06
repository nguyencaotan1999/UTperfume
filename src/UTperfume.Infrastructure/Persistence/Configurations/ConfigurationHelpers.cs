using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UTperfume.Application.Domain;

namespace UTperfume.Infrastructure.Persistence.Configurations;

internal static class ConfigurationHelpers
{
    /// <summary>
    /// Slug CHECK (0002 section 3.12). The BIN2 collation makes the character class case-sensitive and ordinal.
    /// </summary>
    public const string SlugFormatSql =
        "[Slug] NOT LIKE '%[^a-z0-9-]%' COLLATE Latin1_General_BIN2 " +
        "AND [Slug] NOT LIKE '-%' AND [Slug] NOT LIKE '%-' AND [Slug] NOT LIKE '%--%'";

    public const string Utc = "datetime2(3)";

    public static void ConfigureAudit<T>(this EntityTypeBuilder<T> builder) where T : AuditableEntity
    {
        builder.Property(e => e.CreatedAtUtc).HasColumnType(Utc);
        builder.Property(e => e.UpdatedAtUtc).HasColumnType(Utc);
        builder.Property(e => e.UpdatedBy).HasMaxLength(100);
        builder.Property(e => e.RowVersion).IsRowVersion();
    }

    /// <summary>
    /// Sets a database default for the DDL but makes EF always send the value. Without
    /// <c>ValueGeneratedNever</c> EF treats the CLR default (false or 0) as "not set" and omits the column, so the
    /// database default would silently replace an explicit <c>false</c>.
    /// </summary>
    public static PropertyBuilder<TProperty> WithDefault<TProperty>(this PropertyBuilder<TProperty> property, TProperty value) =>
        property.HasDefaultValue(value).ValueGeneratedNever();
}
