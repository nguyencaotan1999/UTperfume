using UTperfume.Application.Common;

namespace UTperfume.Api.Infrastructure;

internal static class ResultMapping
{
    /// <summary>
    /// Maps a handler result to HTTP: 200, 404 (no reason disclosed, ADR-0011) or 400 ValidationProblemDetails keyed
    /// by parameter name.
    /// </summary>
    public static IResult ToHttpResult<T>(this Result<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsSuccess)
        {
            return TypedResults.Ok(result.Value);
        }

        return result.Error!.ToProblem();
    }

    /// <summary>
    /// Maps a handler error to ProblemDetails: 404 (no reason disclosed), 400 ValidationProblemDetails, and for admin
    /// writes 409 and 422 with the machine-readable <c>code</c> extension (0002 section 6.1). Admin endpoints use this
    /// for success statuses other than 200 (201, 204).
    /// </summary>
    public static IResult ToProblem(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return error.Kind switch
        {
            ErrorKind.NotFound => NotFound(),
            ErrorKind.Validation => TypedResults.ValidationProblem(
                error.Details!.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal)),
            ErrorKind.Conflict => CodedProblem(StatusCodes.Status409Conflict, "Conflict", error.Code),
            ErrorKind.Unprocessable => CodedProblem(StatusCodes.Status422UnprocessableEntity, "Unprocessable Entity", error.Code),
            _ => TypedResults.Problem(statusCode: StatusCodes.Status500InternalServerError),
        };
    }

    private static IResult CodedProblem(int status, string title, string? code) =>
        TypedResults.Problem(
            statusCode: status,
            title: title,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = code });

    public static IResult NotFound() =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found");
}
