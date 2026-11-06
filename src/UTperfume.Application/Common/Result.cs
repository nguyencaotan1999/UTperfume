namespace UTperfume.Application.Common;

public enum ErrorKind
{
    NotFound,
    Validation,

    /// <summary>409 with a machine-readable <see cref="Error.Code"/> (admin writes, 0002 section 6.1).</summary>
    Conflict,

    /// <summary>422 business-rule violation with a machine-readable <see cref="Error.Code"/>.</summary>
    Unprocessable,
}

/// <summary>A typed handler error. The API layer maps it to 404, 400, 409 or 422 (ADR-0011).</summary>
public sealed record Error(ErrorKind Kind, IReadOnlyDictionary<string, string[]>? Details = null, string? Code = null);

/// <summary>Small result type for handlers. Exceptions are reserved for unexpected faults.</summary>
public sealed class Result<T>
{
    private Result(T? value, Error? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }

    public Error? Error { get; }

    public bool IsSuccess => Error is null;

    public static Result<T> Ok(T value) => new(value, null);

    public static Result<T> NotFound() => new(default, new Error(ErrorKind.NotFound));

    public static Result<T> Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new(default, new Error(ErrorKind.Validation, errors));

    public static Result<T> Conflict(string code) => new(default, new Error(ErrorKind.Conflict, Code: code));

    public static Result<T> Unprocessable(string code) => new(default, new Error(ErrorKind.Unprocessable, Code: code));

    public static Result<T> Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(default, error);
    }
}
