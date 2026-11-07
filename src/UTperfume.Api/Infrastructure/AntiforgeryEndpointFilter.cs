using Microsoft.AspNetCore.Antiforgery;
using UTperfume.Application.Common;

namespace UTperfume.Api.Infrastructure;

/// <summary>
/// Validates the <c>X-XSRF-TOKEN</c> header on an unsafe admin request (BR-A-04, ADR-0010 section 3). A missing or
/// invalid token returns 400 <c>antiforgery_invalid</c> and the endpoint does not run. P4-02 applies it to the auth
/// endpoints; P4-03 applies it to every admin POST, PUT, PATCH and DELETE.
/// </summary>
internal sealed partial class AntiforgeryEndpointFilter(IAntiforgery antiforgery, ILogger<AntiforgeryEndpointFilter> logger)
    : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            LogRejected(logger, context.HttpContext.Request.Method, context.HttpContext.Request.Path.Value ?? string.Empty);
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "The antiforgery token is missing or invalid.",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = AdminErrorCodes.AntiforgeryInvalid });
        }

        return await next(context);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Antiforgery validation failed for {Method} {Path}.")]
    private static partial void LogRejected(ILogger logger, string method, string path);
}
