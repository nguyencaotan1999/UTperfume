# ADR-0011: RFC 9457 ProblemDetails for all API errors

- Status: Accepted (user approval 2026-11-05). Amended: adds 413/415 for uploads and `antiforgery_invalid` (400).
- Date: 2026-11-05
- Related: architecture §6.2; AC-X1

## Context

The frontend needs one predictable error shape. Errors must not leak internals.

## Decision

- **Shape:** every non-2xx response from `/api/*` is `application/problem+json`. It is produced by `AddProblemDetails()`, `UseExceptionHandler()` with an `IExceptionHandler`, and `UseStatusCodePages()` for empty 404/405 responses. Every response includes a `traceId` extension.
- **400** is `ValidationProblemDetails`, with `errors` keyed by the query or body field name in camelCase.
- **Application layer:** handlers return a small `Result<T>` with typed errors (`NotFound`, `Validation`, `Conflict`, `BusinessRule`). The API layer maps them to 404, 400, 409 and 422. Exceptions are reserved for unexpected faults, which become a generic 500.
- **Error codes:** 409 and 422 responses carry a machine-readable `code` extension (`duplicate_slug`, `duplicate_sku`, `concurrency_conflict`, `publish_requirements_not_met`).
- **Not found:** 404 responses never reveal why a resource is unavailable (draft vs missing).
- **No internal details outside Development:** stack traces and exception messages are only shown in Development.
- **Unknown routes:** unknown `/api/*` routes return 404 ProblemDetails, never the SPA `index.html`.

## Consequences

- The frontend needs one `ApiError` mapper.
- Integration tests assert the content type and the presence of `traceId` for each error status.
