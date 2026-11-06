using UTperfume.Application.Domain;

namespace UTperfume.Application.Common;

/// <summary>The authenticated admin and request facts that audit stamps and audit events need.</summary>
public interface ICurrentAdmin
{
    /// <summary>The Identity user id as text. Throws when there is no authenticated user, so an admin write can never be unattributed.</summary>
    string UserId { get; }

    string? ClientIp { get; }

    string TraceId { get; }
}

/// <summary>
/// Admin write session over the catalogue context (BR-A-10, BR-A-11, BR-A-31). Handlers change tracked entities,
/// declare the row version the client last read, and save through this session so that audit fields, optimistic
/// concurrency and unique-index mapping behave the same for every admin endpoint.
/// </summary>
public interface IAdminWriteSession
{
    /// <summary>Makes the next save fail with <c>concurrency_conflict</c> unless the row still has this version. Also forces the row to be stamped and written.</summary>
    void ExpectRowVersion(AuditableEntity entity, byte[] rowVersion);

    /// <summary>
    /// Stamps UpdatedAtUtc and UpdatedBy (CreatedAtUtc and CreatedBy for inserted images), saves, and maps failures:
    /// a stale row version to 409 <c>concurrency_conflict</c> and a unique-index violation to 409 <c>duplicate_*</c>.
    /// Returns null on success. Throws <see cref="InvalidOperationException"/> when a modified or deleted
    /// auditable entity was not declared with <see cref="ExpectRowVersion"/> (BR-A-10: no last-write-wins).
    /// </summary>
    Task<Error?> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Same as <see cref="SaveChangesAsync"/> but does not require a client row version for modified or deleted entities.
    /// It exists only for server-side touches that have no client-held version. It must NOT be used by client-driven
    /// endpoints, because it gives last-write-wins semantics for the entities it saves.
    /// </summary>
    Task<Error?> SaveServerSideChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Evicts the public output-cache entries (tag <c>catalog</c>) after an admin write that changed public data (BR-A-21).</summary>
public interface IPublicCatalogCache
{
    Task EvictAsync(CancellationToken cancellationToken);
}

/// <summary>Structured audit events for admin writes (BR-A-31). Never takes passwords, cookies or tokens.</summary>
public interface IAdminAudit
{
    void Write(string action, string entityType, object? entityId);
}
