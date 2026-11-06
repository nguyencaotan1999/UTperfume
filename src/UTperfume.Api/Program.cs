using Microsoft.AspNetCore.Authorization;
using UTperfume.Api.Infrastructure;
using UTperfume.Application.Common;
using UTperfume.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApiProblemDetails();
builder.Services.AddValidation();
builder.Services.AddOpenApi();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddOptions<CatalogOptions>()
    .BindConfiguration(CatalogOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddAuthentication(ProblemDetailsAuthenticationHandler.SchemeName)
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ProblemDetailsAuthenticationHandler>(
        ProblemDetailsAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddHealthChecks();
builder.Services.AddInfrastructure(builder.Configuration);

// Fail fast on a missing or invalid storage configuration. The build-time OpenAPI generator
// (GetDocument.Insider) starts the host without any environment configuration, so it is exempt (ADR-0009).
var isOpenApiGeneration = string.Equals(
    System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name, "GetDocument.Insider", StringComparison.Ordinal);
if (!isOpenApiGeneration)
{
    builder.Services.AddOptions<UTperfume.Infrastructure.Storage.ImageStorageOptions>().ValidateOnStart();
}

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddHostedService<DevelopmentStorageInitializer>();
}

var app = builder.Build();

var contentSecurityPolicy = SecurityHeaders.BuildContentSecurityPolicy(
    app.Configuration["Storage:Images:PublicBaseUrl"]);

app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseSecurityHeaders(contentSecurityPolicy);
app.UseStatusCodePages();
app.UseDefaultFiles();
app.UseStaticFiles();
// Static files run BEFORE routing. WebApplication adds UseRouting first unless it is called explicitly, and the
// StaticFileMiddleware skips any request that already has an endpoint selected: the SPA fallback endpoint then
// matched /robots.txt and /assets/*.js and answered with index.html.
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi().AllowAnonymous();

app.MapHealthChecks("/health/live", new() { Predicate = static _ => false }).AllowAnonymous();

var publicApi = app.MapGroup("/api/v1").AllowAnonymous();
publicApi.MapGet("/health", () => TypedResults.Ok(new HealthResponse("ok")))
    .WithName("GetHealth")
    .WithTags("Health")
    .Produces<HealthResponse>();

// Unknown /api routes must be a ProblemDetails 404, never the SPA index.html (AC-S4).
app.Map("/api/{**path}", UnmatchedApiRoute.Handle)
    .AllowAnonymous()
    .ExcludeFromDescription();

// The fallback policy also applies to requests that match no endpoint, so unknown /health routes
// need an explicit anonymous 404 as well; otherwise they would answer 401.
app.Map("/health/{**path}", UnmatchedApiRoute.Handle)
    .AllowAnonymous()
    .ExcludeFromDescription();

// SPA fallback; excludes /api and /health.
app.MapFallbackToFile("{*path:regex(^(?!api(/|$)|health(/|$)).*$)}", "index.html").AllowAnonymous();

app.Run();

/// <summary>Placeholder health payload used by the typed-client sample hook.</summary>
internal sealed record HealthResponse(string Status);

public partial class Program;
