using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using UTperfume.Application.Common;
using UTperfume.Application.Domain;

namespace UTperfume.Infrastructure.Persistence;

/// <summary>
/// Admin write session (BR-A-10, BR-A-11, BR-A-31). Stamps audit fields from <see cref="TimeProvider"/> and the
/// current admin, applies the client's row version as the concurrency token, and maps the two expected
/// database failures to typed errors. Public reads and the development seeder do not use it, so they stay unstamped.
/// </summary>
public sealed class AdminWriteSession(AppDbContext context, TimeProvider timeProvider, ICurrentAdmin admin) : IAdminWriteSession
{
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    /// <summary>Unique index name to error code (database model section 3.11). Identity names are the defaults of P4-02.</summary>
    public static readonly IReadOnlyDictionary<string, string> UniqueIndexCodes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["UX_Brand_Slug"] = AdminErrorCodes.DuplicateSlug,
            ["UX_Brand_Name"] = AdminErrorCodes.DuplicateName,
            ["UX_Category_Slug"] = AdminErrorCodes.DuplicateSlug,
            ["UX_Category_Name"] = AdminErrorCodes.DuplicateName,
            ["UX_Product_Slug"] = AdminErrorCodes.DuplicateSlug,
            ["UX_ProductVariant_Sku"] = AdminErrorCodes.DuplicateSku,
            ["UX_ProductVariant_ProductId_VolumeMl"] = AdminErrorCodes.DuplicateVolume,
            ["UX_FragranceNote_Slug"] = AdminErrorCodes.DuplicateSlug,
            ["UX_FragranceNote_Name"] = AdminErrorCodes.DuplicateName,
            ["UserNameIndex"] = AdminErrorCodes.DuplicateEmail,
            ["EmailIndex"] = AdminErrorCodes.DuplicateEmail,
            // Set-primary race (BR-A-28): two concurrent requests both made an image primary; the loser must retry.
            ["UX_ProductImage_ProductId_Primary"] = AdminErrorCodes.ConcurrencyConflict,
        };

    private readonly HashSet<AuditableEntity> _forceStamp = [];

    public void ExpectRowVersion(AuditableEntity entity, byte[] rowVersion)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(rowVersion);

        context.Entry(entity).Property(e => e.RowVersion).OriginalValue = rowVersion;
        _forceStamp.Add(entity);
    }

    public Task<Error?> SaveChangesAsync(CancellationToken cancellationToken) => SaveCoreAsync(requireRowVersion: true, cancellationToken);

    public Task<Error?> SaveServerSideChangesAsync(CancellationToken cancellationToken) => SaveCoreAsync(requireRowVersion: false, cancellationToken);

    private async Task<Error?> SaveCoreAsync(bool requireRowVersion, CancellationToken cancellationToken)
    {
        Stamp(requireRowVersion);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            _forceStamp.Clear();
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return new Error(ErrorKind.Conflict, Code: AdminErrorCodes.ConcurrencyConflict);
        }
        catch (DbUpdateException exception) when (TryMapUniqueViolation(exception, out var code))
        {
            return new Error(ErrorKind.Conflict, Code: code);
        }
    }

    private void Stamp(bool requireRowVersion)
    {
        context.ChangeTracker.DetectChanges();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var entries = context.ChangeTracker.Entries().ToList();
        string? userId = null;

        if (requireRowVersion)
        {
            foreach (var entry in entries)
            {
                if (entry.Entity is AuditableEntity undeclared
                    && entry.State is EntityState.Modified or EntityState.Deleted
                    && !_forceStamp.Contains(undeclared))
                {
                    throw new InvalidOperationException(
                        $"{entry.Metadata.ClrType.Name} was {entry.State.ToString().ToLowerInvariant()} without IAdminWriteSession.ExpectRowVersion. "
                        + "Declare the client's row version before saving, or use SaveServerSideChangesAsync for a server-side touch.");
                }
            }
        }

        foreach (var entry in entries)
        {
            switch (entry.Entity)
            {
                case AuditableEntity auditable when entry.State is EntityState.Added or EntityState.Modified
                                                    || (entry.State == EntityState.Unchanged && _forceStamp.Contains(auditable)):
                    userId ??= admin.UserId;
                    if (entry.State == EntityState.Added)
                    {
                        auditable.CreatedAtUtc = now;
                    }

                    SetModified(entry, nameof(AuditableEntity.UpdatedAtUtc), now);
                    SetModified(entry, nameof(AuditableEntity.UpdatedBy), userId);
                    break;

                case ProductImage image when entry.State == EntityState.Added:
                    userId ??= admin.UserId;
                    image.CreatedAtUtc = now;
                    image.CreatedBy = userId;
                    break;

                case FragranceNote note when entry.State == EntityState.Added && note.CreatedAtUtc == default:
                    note.CreatedAtUtc = now;
                    break;
            }
        }
    }

    private static void SetModified(EntityEntry entry, string property, object? value)
    {
        var propertyEntry = entry.Property(property);
        propertyEntry.CurrentValue = value;
        if (entry.State != EntityState.Added)
        {
            propertyEntry.IsModified = true;
        }
    }

    private static bool TryMapUniqueViolation(DbUpdateException exception, out string code)
    {
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is SqlException { Number: UniqueIndexViolation or UniqueConstraintViolation } sql)
            {
                foreach (var (index, mapped) in UniqueIndexCodes)
                {
                    if (sql.Message.Contains($"'{index}'", StringComparison.OrdinalIgnoreCase))
                    {
                        code = mapped;
                        return true;
                    }
                }
            }
        }

        code = string.Empty;
        return false;
    }
}
