using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace UTperfume.Api.Infrastructure;

/// <summary>
/// Converts exceptions to RFC 9457 responses (ADR-0011). A <see cref="BadHttpRequestException"/> keeps its own
/// status (400, 413, 415, ...) with a generic ProblemDetails body; everything else is a generic 500. Exception
/// details are only included in the Development environment. Field-level errors never come from here: validation
/// is done in code and returns ValidationProblemDetails itself.
/// </summary>
internal sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is BadHttpRequestException { StatusCode: >= 400 and < 500 } badRequest)
        {
            return await WriteClientErrorAsync(httpContext, badRequest);
        }

        LogUnhandled(logger, exception, httpContext.Request.Method, httpContext.Request.Path.Value ?? string.Empty);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Detail = environment.IsDevelopment() ? exception.Message : null,
        };

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = environment.IsDevelopment() ? exception : null,
        });
    }

    private async ValueTask<bool> WriteClientErrorAsync(HttpContext httpContext, BadHttpRequestException exception)
    {
        httpContext.Response.StatusCode = exception.StatusCode;

        var problem = new ProblemDetails
        {
            Status = exception.StatusCode,
            Title = exception.StatusCode switch
            {
                StatusCodes.Status400BadRequest => "Bad Request",
                StatusCodes.Status413PayloadTooLarge => "Payload Too Large",
                StatusCodes.Status415UnsupportedMediaType => "Unsupported Media Type",
                _ => "Request Error",
            },
            Detail = environment.IsDevelopment() ? exception.Message : null,
        };

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for {Method} {Path}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method, string path);
}
