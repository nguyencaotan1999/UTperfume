# ADR-0007: API route versioning and admin boundary

- Status: Accepted (user approval 2026-11-05). Amended: `/api/v1/admin/auth/*` sign-in and reset endpoints are anonymous by design (an explicit allowlist), and every unsafe admin method requires antiforgery (ADR-0010).
- Date: 2026-11-05
- Related: architecture §6.4–6.5; BR-27, BR-28; AC-A1 to AC-A4

## Context

The public catalogue is anonymous. Catalogue management must be restricted to staff. Authorization must be enforced on the server, and new endpoints must not be accidentally public.

## Decision

- All API routes are under `/api/v1`. Public routes are resource-oriented (`/products`, `/brands`, `/categories`, `/catalog/filters`, `/home`).
- Admin routes are under `/api/v1/admin/...` in a single route group with `.RequireAuthorization("AdminOnly")`, `Cache-Control: no-store`, and no output caching.
- **Secure by default:** `FallbackPolicy` requires an authenticated user. Public groups opt out explicitly with `.AllowAnonymous()`.
- Public and admin DTOs are separate types. Admin DTOs may expose IDs, status, flags and `rowVersion`. Public DTOs never do.
- A test enumerates every endpoint under `/api/v1/admin` and asserts it carries `AdminOnly`. Another test asserts that every public endpoint is anonymous by design.
- The admin UI is a lazy-loaded route tree (`/admin/*`) inside the same SPA. Its guards are for usability only.

Rejected alternatives:

- **A separate admin API host:** more infrastructure for no current benefit.
- **Per-endpoint `[Authorize]` without a fallback policy:** fragile, because one missed attribute exposes an endpoint.

## Consequences

- `v1` means breaking changes need `/api/v2` or explicit approval. This is visible through the OpenAPI diff (ADR-0009).
- The admin and public contracts can evolve independently.
