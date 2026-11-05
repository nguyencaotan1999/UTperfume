using System.Diagnostics;

namespace UTperfume.Api.Infrastructure;

internal static class ProblemDetailsSetup
{
    public const string TraceIdKey = "traceId";

    public static IServiceCollection AddApiProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
            {
                // Every problem response carries a trace id (AC-X1).
                context.ProblemDetails.Extensions[TraceIdKey] =
                    Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
            });
        services.AddExceptionHandler<GlobalExceptionHandler>();
        return services;
    }
}
