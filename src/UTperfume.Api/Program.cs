using Microsoft.AspNetCore.Authorization;
using UTperfume.Api.Endpoints;
using UTperfume.Api.Endpoints.Admin;
using UTperfume.Api.Infrastructure;
using UTperfume.Application;
using UTperfume.Application.Common;
using UTperfume.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApiProblemDetails();
builder.Services.AddValidation();
builder.Services.AddCatalogFeatures();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    // Strict: numbers are JSON numbers only. The default (AllowReadingFromString) made the OpenAPI generator
    // describe every integer as "integer or string", which leaks into the generated TypeScript types.
    o.SerializerOptions.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict;
});
builder.Services.AddOpenApi(o => o.AddOperationTransformer<PublicQueryParameterTransformer>());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddPublicTraffic();
builder.Services.AddAdminWriteFoundation();
builder.Services.AddOptions<UTperfume.Application.Features.Sitemap.SitemapOptions>()
    .BindConfiguration(UTperfume.Application.Features.Sitemap.SitemapOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<StoreOptions>, StoreOptionsValidator>();
builder.Services.AddOptions<StoreOptions>().BindConfiguration(StoreOptions.SectionName);

builder.Services.AddOptions<CatalogOptions>()
    .BindConfiguration(CatalogOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddAuthentication(ProblemDetailsAuthenticationHandler.SchemeName)
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ProblemDetailsAuthenticationHandler>(
        ProblemDetailsAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddAdminPolicy();

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, tags: ["ready"], timeout: TimeSpan.FromSeconds(5));
builder.Services.AddInfrastructure(builder.Configuration);

// Fail fast on a missing or invalid storage configuration. The build-time OpenAPI generator
// (GetDocument.Insider) starts the host without any environment configuration, so it is exempt (ADR-0009).
var isOpenApiGeneration = string.Equals(
    System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name, "GetDocument.Insider", StringComparison.Ordinal);
if (!isOpenApiGeneration)
{
    builder.Services.AddOptions<UTperfume.Infrastructure.Storage.ImageStorageOptions>().ValidateOnStart();
    // Outside Development a missing or malformed Store:PublicBaseUrl stops the start-up with a clear message.
    builder.Services.AddOptions<StoreOptions>().ValidateOnStart();
}

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddHostedService<DevelopmentStorageInitializer>();
    builder.Services.AddDevelopmentSeeding(builder.Environment.ContentRootPath);
    builder.Services.AddHostedService<DevelopmentDataInitializer>();
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
app.UseApiCacheControl();
app.UseStatusCodePages();
app.UseDefaultFiles();
app.UseStaticFiles();
// Static files run BEFORE routing. WebApplication adds UseRouting first unless it is called explicitly, and the
// StaticFileMiddleware skips any request that already has an endpoint selected: the SPA fallback endpoint then
// matched /robots.txt and /assets/*.js and answered with index.html.
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
// The limiter runs before the output cache, so cached responses still count against the limit (AC-X3).
app.UseRateLimiter();
app.UseOutputCache();

app.MapOpenApi().AllowAnonymous();

app.MapHealthChecks("/health/live", new() { Predicate = static _ => false }).AllowAnonymous()
    .WithMetadata(new Microsoft.AspNetCore.Routing.HttpMethodMetadata(["GET", "HEAD"]));

// E11 readiness: checks the database and answers 503 (Unhealthy) when it is down. The body is the bare status, never details.
app.MapHealthChecks("/health/ready", new() { Predicate = static r => r.Tags.Contains("ready") }).AllowAnonymous()
    .WithMetadata(new Microsoft.AspNetCore.Routing.HttpMethodMetadata(["GET", "HEAD"]));

app.MapSitemapEndpoints();

var publicApi = app.MapGroup("/api/v1").AllowAnonymous()
    .RequireRateLimiting(PublicTrafficSetup.PolicyName)
    .CacheOutput(PublicTrafficSetup.PolicyName)
    .ProducesProblem(StatusCodes.Status429TooManyRequests);
publicApi.MapGet("/health", () => TypedResults.Ok(new HealthResponse("ok")))
    .WithName("GetHealth")
    .WithTags("Health")
    .Produces<HealthResponse>();

publicApi.MapBrandEndpoints();
publicApi.MapCategoryEndpoints();
publicApi.MapProductEndpoints();
publicApi.MapCatalogFilterEndpoints();
publicApi.MapHomeEndpoints();

app.MapAdminEndpoints();

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
