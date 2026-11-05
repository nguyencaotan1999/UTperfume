# ADR-0012: Three-project backend, Minimal APIs, single same-origin App Service

- Status: Accepted (user approval 2026-11-05). Amended: one `AppDbContext` covering the `catalog` and `identity` schemas.
- Date: 2026-11-05
- Related: architecture §2, §4

## Context

This is a greenfield catalogue application with a small domain. The rules are to keep it simple, avoid speculative abstractions, and stay on the approved stack. Azure Static Web Apps is not in the approved stack.

## Decision

1. **Projects:**
   - `UTperfume.Api`, `UTperfume.Application` (with domain entities in `Domain/`), `UTperfume.Infrastructure`
   - Test projects: `UTperfume.UnitTests`, `UTperfume.IntegrationTests`
   - Dependency direction: Api → Application ← Infrastructure. Api references Infrastructure only for DI composition.
   - Feature folders inside each project.
2. **Data access:**
   - No repository layer.
   - Handlers use `ICatalogDbContext` (`IQueryable` sets) with projections.
   - `Application` references the base `Microsoft.EntityFrameworkCore` package only.
3. **Minimal APIs** with `MapGroup`, `TypedResults` and the .NET 10 built-in validation. FluentValidation and MediatR are not used.
4. **Hosting:**
   - One App Service hosts the API and serves the built SPA from `wwwroot`, using `MapFallbackToFile("index.html")`, which excludes `/api` and `/health`.
   - Same origin, so no CORS.
   - Migrations never run at startup outside Development.
5. **Central Package Management** and `Directory.Build.props` with nullable enabled and warnings treated as errors.

Rejected alternatives:

- **A separate Domain project:** no benefit yet.
- **Controllers:** more ceremony. Minimal APIs cover the needs of this application.
- **Separate frontend hosting:** adds CORS and needs a second service.

## Consequences

- The project count stays low, and onboarding is simple.
- If the domain grows, splitting out `Domain/` is a mechanical refactor.
- SPA and API deploy together. A frontend-only change still redeploys the app, which is acceptable at this size.
