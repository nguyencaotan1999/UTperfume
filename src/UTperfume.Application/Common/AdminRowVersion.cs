namespace UTperfume.Application.Common;

/// <summary>
/// The <c>rowVersion</c> of admin DTOs is the SQL Server <c>rowversion</c> as a base64 string (BR-A-10).
/// A missing, blank or malformed value is a 400 on the field <c>rowVersion</c>.
/// </summary>
public static class AdminRowVersion
{
    public const string FieldName = "rowVersion";

    public static string Encode(byte[] rowVersion)
    {
        ArgumentNullException.ThrowIfNull(rowVersion);
        return Convert.ToBase64String(rowVersion);
    }

    public static Result<byte[]> Decode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Invalid("The rowVersion is required.");
        }

        var buffer = new byte[value.Length];
        if (!Convert.TryFromBase64String(value, buffer, out var written) || written == 0)
        {
            return Invalid("The rowVersion is not valid.");
        }

        return Result<byte[]>.Ok(buffer.AsSpan(0, written).ToArray());
    }

    private static Result<byte[]> Invalid(string message) =>
        Result<byte[]>.Invalid(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [FieldName] = [message],
        });
}
