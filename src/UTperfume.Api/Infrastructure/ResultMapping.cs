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

        return result.Error!.Kind switch
        {
            ErrorKind.NotFound => NotFound(),
            ErrorKind.Validation => TypedResults.ValidationProblem(
                result.Error.Details!.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal)),
            _ => TypedResults.Problem(statusCode: StatusCodes.Status500InternalServerError),
        };
    }

    public static IResult NotFound() =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found");
}
