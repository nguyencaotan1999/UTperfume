using Microsoft.AspNetCore.Antiforgery;
using UTperfume.Application.Common;

namespace UTperfume.Api.Infrastructure;

/// <summary>
/// Endpoint metadata that records that the admin antiforgery requirement applies to an endpoint. Endpoint filters are
/// compiled into the request delegate and are not visible in the endpoint data source, so the middleware and the
/// enumeration test (AC-A-38) read this marker.
/// </summary>
internal sealed class RequiresAdminAntiforgeryMetadata
{
    public static RequiresAdminAntiforgeryMetadata Instance { get; } = new();
}

internal static class AdminAntiforgery
{
    /// <summary>Set on the request once the token was validated, so the endpoint filter does not validate a second time.</summary>
    internal const string ValidatedItemKey = "utp.admin_antiforgery_validated";

    public static bool IsUnsafeMethod(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

    /// <summary>
    /// Applies the antiforgery requirement to every endpoint of the group, including endpoints added later (P4-03,
    /// BR-A-04). <see cref="AdminAntiforgeryMiddleware"/> validates before parameter binding, so a JSON body or a
    /// multipart form is not read without a valid token. The endpoint filter is kept as defence in depth: it validates
    /// (after binding) only when the middleware did not run, for example when the pipeline is misconfigured.
    /// </summary>
    public static TBuilder RequireAdminAntiforgery<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.Add(endpoint => endpoint.Metadata.Add(RequiresAdminAntiforgeryMetadata.Instance));
        builder.AddEndpointFilter<TBuilder, AntiforgeryEndpointFilter>();
        return builder;
    }

    /// <summary>
    /// True when the request carries a valid <c>X-XSRF-TOKEN</c> header token. The header is required (BR-A-04): without
    /// it the request is rejected before <c>ValidateRequestAsync</c>, so the antiforgery token store never falls back to
    /// reading a form field and the body is not read.
    /// </summary>
    public static async Task<bool> IsValidAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (string.IsNullOrWhiteSpace(context.Request.Headers[AdminIdentitySetup.XsrfHeaderName].ToString()))
        {
            return false;
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context);
            context.Items[ValidatedItemKey] = true;
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }

    public static IResult Rejected() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Bad Request",
            detail: "The antiforgery token is missing or invalid.",
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = AdminErrorCodes.AntiforgeryInvalid });
}

/// <summary>
/// Validates the <c>X-XSRF-TOKEN</c> header on an unsafe admin request (BR-A-04, ADR-0010 section 3) before the endpoint
/// is invoked. Endpoint filters run after parameter binding, so only middleware can reject a forged request before a
/// JSON body or an <c>IFormFile</c> multipart form is read. Placed after authentication and authorization.
/// </summary>
internal sealed partial class AdminAntiforgeryMiddleware(RequestDelegate next, ILogger<AdminAntiforgeryMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, IAntiforgery antiforgery)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(antiforgery);

        if (context.GetEndpoint()?.Metadata.GetMetadata<RequiresAdminAntiforgeryMetadata>() is not null
            && AdminAntiforgery.IsUnsafeMethod(context.Request.Method)
            && !await AdminAntiforgery.IsValidAsync(context, antiforgery))
        {
            LogRejected(logger, context.Request.Method, context.Request.Path.Value ?? string.Empty);
            await AdminAntiforgery.Rejected().ExecuteAsync(context);
            return;
        }

        await next(context);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Antiforgery validation failed for {Method} {Path}.")]
    private static partial void LogRejected(ILogger logger, string method, string path);
}

/// <summary>Defence in depth behind <see cref="AdminAntiforgeryMiddleware"/>; runs after parameter binding.</summary>
internal sealed partial class AntiforgeryEndpointFilter(IAntiforgery antiforgery, ILogger<AntiforgeryEndpointFilter> logger)
    : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        // The group-level registration runs for every method; only unsafe methods need a token.
        if (!AdminAntiforgery.IsUnsafeMethod(context.HttpContext.Request.Method)
            || context.HttpContext.Items.ContainsKey(AdminAntiforgery.ValidatedItemKey)
            || await AdminAntiforgery.IsValidAsync(context.HttpContext, antiforgery))
        {
            return await next(context);
        }

        LogRejected(logger, context.HttpContext.Request.Method, context.HttpContext.Request.Path.Value ?? string.Empty);
        return AdminAntiforgery.Rejected();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Antiforgery validation failed for {Method} {Path}.")]
    private static partial void LogRejected(ILogger logger, string method, string path);
}
