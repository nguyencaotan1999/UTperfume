using System.Globalization;
using Microsoft.Extensions.Logging;

namespace UTperfume.Application.Common;

/// <summary>Action names of audit events (BR-A-31). Free text is never logged.</summary>
public static class AdminAuditActions
{
    public const string Create = "create";
    public const string Update = "update";
    public const string Delete = "delete";
    public const string Publish = "publish";
    public const string Archive = "archive";
    public const string ImageUpload = "image_upload";
    public const string ImageDelete = "image_delete";
}

/// <summary>
/// Writes one structured log event per admin write with the action, entity type and ID, acting user ID, client IP and
/// trace ID (BR-A-31). The only inputs are those fields, so no password, cookie, token or email address can reach the log.
/// </summary>
public sealed partial class AdminAudit(ICurrentAdmin admin, ILogger<AdminAudit> logger) : IAdminAudit
{
    public void Write(string action, string entityType, object? entityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);

        LogEvent(
            logger,
            action,
            entityType,
            Convert.ToString(entityId, CultureInfo.InvariantCulture),
            admin.UserId,
            admin.ClientIp,
            admin.TraceId);
    }

    [LoggerMessage(
        EventId = 5000,
        Level = LogLevel.Information,
        Message = "Admin audit: {Action} {EntityType} {EntityId} by user {UserId} from {ClientIp} (trace {TraceId})")]
    private static partial void LogEvent(
        ILogger logger, string action, string entityType, string? entityId, string userId, string? clientIp, string traceId);
}
