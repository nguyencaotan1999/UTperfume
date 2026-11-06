namespace UTperfume.Application.Common;

public enum ErrorKind
{
    NotFound,
    Validation,
}

/// <summary>A typed handler error. The API layer maps it to 404 or 400 (ADR-0011).</summary>
public sealed record Error(ErrorKind Kind, IReadOnlyDictionary<string, string[]>? Details = null);

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
}
