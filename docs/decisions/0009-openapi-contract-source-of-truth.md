# ADR-0009: OpenAPI generated from code is the contract source of truth

- Status: Accepted (user approval 2026-11-05). TanStack Query is approved as the data-fetching layer over `openapi-fetch` (architecture §5.2).
- Date: 2026-11-05
- Related: architecture §4.3, §10; CLAUDE.md ("Use OpenAPI as the source of truth", "Keep backend and frontend contracts synchronized")

## Context

The backend and frontend must stay in sync, and public API changes must never be silent.

## Decision

- **Generation:** the API uses the built-in `Microsoft.AspNetCore.OpenApi`. `Microsoft.Extensions.ApiDescription.Server` writes `openapi/v1.json` at build time, and the file is committed.
- **Explicit contract:** endpoints declare `TypedResults` and `.Produces/.ProducesProblem`, so every status code and shape is documented.
- **Frontend types:** `npm run generate:api` runs `openapi-typescript` and writes `web/src/api/schema.d.ts` (committed). The frontend calls the API through `openapi-fetch`, which is typed by that schema.
- **CI drift check:** CI regenerates both files and runs `git diff --exit-code`. A PR that changes the contract shows the change in `openapi/v1.json`, and the reviewer must check it for breaking changes.
- **No interactive API UI by default.** Scalar or Swagger UI can be added as a Development-only dependency through a later ADR.

Rejected alternatives:

- **A hand-written spec (design-first):** it duplicates effort and drifts from the code.
- **Generated client classes (NSwag/Kiota):** heavier, and not needed with typed fetch.

## Consequences

- Build-time document generation runs the app's startup in a special mode. `Program.cs` must not connect to the database or Key Vault during service registration.
- Two new packages: `openapi-typescript` (dev) and `openapi-fetch` (runtime).
