# 0002 – Admin Area Requirements (Catalogue Management)

- Status: **Approved** by the user on 2026-11-06 (P4-04). All decision points (D-A1 to D-A4, Q-12, Q-14) are resolved; see §9.
- Date: 2026-11-06 (draft and approval); revised 2026-11-07 for OP-1, OP-2 and the P4-25 decision (option A: password-reset flow covered by integration tests, not Playwright); revised 2026-11-07 for the P4-02 review decisions D-A5 (sign-out refreshes the security stamp) and D-A6 (bootstrap approval switch), and review item L-2 (no 429 on `GET /auth/antiforgery`)
- Author: Architect
- Related: `docs/requirements/0001-perfume-catalogue-requirements.md` (BR-27 to BR-30, AC-A1 to AC-A10), `docs/architecture/0001-solution-architecture.md` §6.5, §6.6, §7, `docs/architecture/0002-database-model.md`, ADR-0005, ADR-0007, ADR-0010, ADR-0011, ADR-0014, `docs/tasks/0001-phase-plan.md` (Phase 4)

## 1. Requirement Summary

The admin area lets staff manage the catalogue that the public storefront shows. It covers:

- Admin sign-in and sign-out with ASP.NET Core Identity and a same-origin cookie session (ADR-0010).
- Admin user management: create, deactivate, reactivate, unlock, and admin-assisted password reset.
- Management of brands, categories, products (with variants, stock, categories and fragrance notes), product images, fragrance notes and home banners.
- Image upload to Azure Blob Storage through the API (ADR-0014).

It builds on the admin boundary already specified in `0001` (BR-27 to BR-30, AC-A1 to AC-A10). Those rules are not repeated here; this document refers to them. Identifiers in this document use the forms `BR-A-nn` and `AC-A-nn`, so they do not collide with `AC-A1` to `AC-A10` in `0001`.

No new dependency and no change to the approved stack are introduced. The database tables already exist (`InitialCatalog`) or are planned (`AddAdminIdentity`, P4-02). This document requires **no further schema change**. No persistent audit table is added (D-A3).

## 2. Scope

### In scope

- Admin API under `/api/v1/admin/*` and the admin UI under `/admin/*` (lazy-loaded, same SPA).
- Sign-in, sign-out, session, antiforgery, current-admin probe (`GET /admin/me`).
- Password reset by email and change password (P4-05; production sender P4-05a), as already specified in `0001` AC-A9 and architecture §7.3.
- Admin user management (create, list, deactivate, reactivate, unlock, set a temporary password).
- CRUD for brands and categories, including one logo or image each.
- Products: create, edit, publish, archive, delete (restricted, BR-A-14), category assignment, fragrance-note assignment.
- Variants: create, edit (price, compare-at price, **stock quantity**, active flag). No delete.
- Product images: upload, edit metadata (alt text, order, primary), delete.
- Fragrance-note master list: create, rename, delete when unused.
- Home banners: CRUD including the banner image.
- Concentrations: **read-only** list for drop-downs (BR-A-19).
- Audit fields and structured audit log events (BR-A-31). No persistent audit history table (D-A3).

### Out of scope

- Customer accounts, orders, checkout and anything else listed as future work in `0001`.
- Stock movement ledger, stock reservation, stock adjustments by delta (ADR-0005). Stock is set as an absolute number.
- Bulk import or export (CSV, Excel), bulk edit.
- Image editing, resizing, thumbnail generation, re-encoding or metadata stripping (would need a new dependency).
- Old-slug redirects (Q-08), product revision history, scheduled publishing.
- Editing concentrations through the admin (they are seeded by migration).
- Self-registration of admins, external sign-in providers, enforced two-factor sign-in (Q-14: not required before go-live, decided 2026-11-06; risk R-A-07).
- Separate roles or permission levels inside the admin area (D-A1).
- A persistent audit history table (D-A3).
- Production configuration of email, Data Protection keys or the first admin (Phase 5, needs user approval).

## 3. Actors

| Actor | Description | Authentication |
|---|---|---|
| Admin | Staff member holding the `Catalog.Admin` role | Identity cookie session (ADR-0010) |
| Anonymous caller | Anyone before sign-in | Only the allowlisted `auth/*` endpoints (BR-A-05) |
| Authenticated non-admin | A signed-in Identity user without `Catalog.Admin` (not created by any flow today, but tested) | Receives 403 on every admin endpoint except the allowlist |

## 4. User Stories

| ID | Story |
|---|---|
| US-A-01 | As an admin, I want to sign in with email and password and sign out, so only staff can change the catalogue. |
| US-A-02 | As an admin, I want to create other admin accounts and deactivate them when staff leave. |
| US-A-03 | As an admin, I want to unlock a locked-out colleague or give them a temporary password. |
| US-A-04 | As an admin, I want to create and edit brands and categories, including a logo or image, and hide them without deleting them. |
| US-A-05 | As an admin, I want to create a product as a draft, add variants, images, categories and fragrance notes, and publish it only when it is complete. |
| US-A-06 | As an admin, I want to set each variant's price, compare-at price and stock quantity. |
| US-A-07 | As an admin, I want to upload product images, choose the primary image and the order, and remove images. |
| US-A-08 | As an admin, I want to archive a product so it disappears from the storefront without losing its data. |
| US-A-09 | As an admin, I want to manage the home banners. |
| US-A-10 | As an admin, I want to be told when a colleague changed the same record before me, instead of silently overwriting their work. |

## 5. Business Rules

### 5.1 Sign-in, session and accounts

- **BR-A-01: Sign-in.**
  - Input is email and password. The email is trimmed and compared case-insensitively (Identity normalised email).
  - Success returns 200 with the current-admin DTO and sets the `__Host-utp-admin` cookie.
  - Unknown email, wrong password, locked account and **deactivated** account all return the same 401 ProblemDetails (`0001` AC-A6, AC-A7).
  - A user without `Catalog.Admin` cannot sign in to the admin area; this also returns the same 401.
  - Lockout and password policy are as `0001` BR-29 and architecture §7.1. Sign-in is rate-limited (`RateLimiting:AuthPerMinute`, default 5 per IP per minute; 429).
- **BR-A-02: Session.** Cookie `HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/`; 30 minutes sliding idle timeout; 8 hours absolute; no "remember me" (`0001` AC-A8). An expired session yields 401 on the next admin call, and the UI redirects to `/admin/login`.
- **BR-A-03: Sign-out.** `POST /admin/auth/sign-out` clears the session cookie and returns 204. It is idempotent: calling it without a session also returns 204.
  - **Decided (D-A5, user 2026-11-07, option (b)):** when the request carries a valid session, sign-out also **refreshes the signed-in user's security stamp**. A copy of the session cookie (for example taken from a shared machine or a log) therefore stops working at the next security-stamp revalidation, at the latest 5 minutes after sign-out (normal case; see R-A-09 for the fail-open case) (BR-A-09, ADR-0010 §2). Without a session nothing is refreshed and the call still returns 204.
  - **Accepted side effect:** the refreshed stamp also ends **every other session of the same user** (other browsers or devices) within the same 5-minute interval (normal case; see R-A-09 for the fail-open case). Sign-out is therefore "sign out everywhere" for that user. Risk R-A-09.
- **BR-A-04: Antiforgery.**
  - Every POST, PUT, PATCH and DELETE under `/api/v1/admin`, **including sign-in, sign-out and the password-reset endpoints**, requires a valid `X-XSRF-TOKEN` header (ADR-0010 §3).
  - A missing or invalid token returns 400 ProblemDetails with `code=antiforgery_invalid`, and nothing is changed (`0001` AC-A5).
  - The SPA fetches a token from `GET /admin/auth/antiforgery` before sign-in and **again after sign-in and after sign-out**, because tokens are bound to the user identity.
  - Multipart image uploads are unsafe methods and are covered by the same rule.
- **BR-A-05: Anonymous allowlist.** Only these admin endpoints are anonymous. Every other `/api/v1/admin/*` endpoint requires `AdminOnly`, including `auth/change-password`:
  - `GET /admin/auth/antiforgery`
  - `POST /admin/auth/sign-in`
  - `POST /admin/auth/sign-out`
  - `POST /admin/auth/password-reset/request`
  - `POST /admin/auth/password-reset/confirm`
- **BR-A-06: Roles.** One role, `Catalog.Admin`. Every holder can perform every operation in this document, including admin user management.
  - **Decided (D-A1, user 2026-11-06):** one Admin role for all admin functions. No separate role for admin user management.
- **BR-A-07: Admin users.**
  - No self-registration. An admin creates another admin with email, display name (1–100 characters) and a temporary password that meets the password policy. The new user gets `Catalog.Admin`.
  - The email must be unique (409 `duplicate_email`).
  - Admin users are never hard-deleted. They are deactivated (`IsActive = false`) and can be reactivated.
  - **Unlock** clears `LockoutEnd` and `AccessFailedCount`.
  - **Set temporary password** (admin-assisted reset, architecture §7.3) replaces the password and refreshes the security stamp. It is the production reset path until the email provider decided in Q-12 is wired and its credential is configured in the deployment secret store (P4-05a, Phase 5), and it remains available afterwards as a fallback.
- **BR-A-08: Self-protection.** An admin cannot deactivate their own account (422 `cannot_deactivate_self`). The last active admin cannot be deactivated (422 `last_active_admin`).
- **BR-A-09: Session revocation.** Deactivation, a password change, a password reset, setting a temporary password and sign-out with a valid session (BR-A-03, D-A5) all refresh the user's security stamp. Existing sessions of that user stop working within 5 minutes (security-stamp validation interval, ADR-0010). For sign-out this is the normal case; see R-A-09 for the fail-open case.

### 5.2 Catalogue management

- **BR-A-10: Optimistic concurrency.**
  - Brand, Category, Product, ProductVariant and HomeBanner carry `rowVersion` (base64 string) in admin DTOs.
  - Every update and every state action (publish, archive) on these entities must send the `rowVersion` last read. Missing returns 400; stale returns 409 `concurrency_conflict` and nothing is changed.
  - FragranceNote and ProductImage have no `rowVersion` column (database model §3.6, §3.7). Their updates are last-write-wins. This is accepted because they hold little data.
  - **Decided (P4-06, Manager 2026-11-07):** image deletes are also last-write-wins: `DELETE /products/{id}/images/{imageId}` takes no `rowVersion`. A repeated delete of the same image (or a delete of an image already removed by another admin) returns 404, as in §6.5.
- **BR-A-11: Field validation** follows architecture §6.6 and `0001` BR-05 to BR-07, BR-23 and BR-25:
  - Names are required and trimmed, with the maximum lengths of the database model.
  - The slug is optional on create and is then generated from the name (`0001` BR-25). A supplied slug must match the slug format.
  - Duplicate slug, name or SKU returns 409 with `code` `duplicate_slug`, `duplicate_name` or `duplicate_sku`. A unique-index violation from a concurrent insert maps to the same 409 (database model §3.11).
  - Descriptions are plain text, never HTML (`0001` A-08).
- **BR-A-12: Brands and categories.**
  - `IsActive = false` hides the brand or category publicly (`0001` BR-01, BR-02). Deactivating a brand hides all its products; this is allowed and is not blocked.
  - Hard delete is allowed only when nothing references the row: no product for a brand; no product link for a category. Otherwise 422 `in_use`, and the admin deactivates instead.
- **BR-A-13: Product lifecycle.**
  - A new product is created as **Draft**.
  - Allowed transitions:

    | Action | From | To |
    |---|---|---|
    | `publish` | Draft, Archived | Published |
    | `archive` | Draft, Published | Archived |

  - Calling an action on a product already in the target state returns 200 and changes nothing.
  - **Publish requires** an active brand, at least one active variant and at least one image (architecture §6.6). Otherwise 422 `publish_requirements_not_met`, with the list of unmet requirements in the ProblemDetails.
  - `PublishedAtUtc` is set on the **first** publish only and kept when an archived product is published again, so "new arrivals" ordering does not jump.
  - There is no "unpublish to Draft". To hide a product, archive it.
- **BR-A-14: Product delete.** Only a Draft product that has **never** been published (`PublishedAtUtc` is null) can be hard-deleted. Its variants, image rows, category links and note links are removed in one transaction, and its blobs are deleted afterwards on a best-effort basis. Otherwise 422 `product_was_published`; archive instead. This keeps published SKUs available for future orders (database model §3.5).
- **BR-A-15: Published-product invariants.** While a product is Published:
  - its last active variant cannot be deactivated, and
  - its last image cannot be deleted.

  Either attempt returns 422 `published_product_requirement`. The admin archives the product first. Deactivating the product's **brand** is still allowed (BR-A-12).
- **BR-A-16: Variants.**
  - No delete endpoint. Variants are deactivated (`isActive = false`), because future orders will reference them.
  - The SKU is stored uppercase (`0001` BR-05). The volume is unique per product (`0001` BR-06; 409 `duplicate_volume`).
  - **Stock quantity** is set as an absolute integer >= 0. There are no increments or decrements in this release (ADR-0005).
  - `stockQuantity` appears **only** in admin DTOs. Admin responses also show the derived `availability`, using the same `Catalog:LowStockThreshold` as the storefront.
- **BR-A-17: Fragrance notes.**
  - Product notes are replaced as a whole: `PUT /admin/products/{id}/notes` with `top`, `heart` and `base` arrays of fragrance-note IDs. Array order is the display order.
  - A note appears at most once per tier (400 on duplicates). Unknown IDs return 400 with the offending field in `errors`.
  - The master list allows create and rename. Delete is allowed only when no product uses the note; otherwise 422 `in_use`.
- **BR-A-18: Product categories** are replaced as a whole through `categoryIds` on product create and update. Unknown IDs return 400. Inactive categories may be assigned.
- **BR-A-19: Concentrations are read-only** in the admin (`GET /admin/concentrations`). They are seeded with `HasData`, and editing them through the API would conflict with the next migration. *This narrows architecture §6.5, which listed CRUD for concentrations;* architecture §6.5 was updated on approval (2026-11-06).
- **BR-A-20: Banners.** Title and alt text are required; exactly one image; `linkUrl` must be site-relative (`0001` BR-23); `endsAtUtc` must be after `startsAtUtc` when both are set. Banners can be hard-deleted, and the blob is deleted afterwards.
- **BR-A-21: Public cache.** A successful admin write evicts the affected public output-cache entries on the instance (architecture §6.1). Browsers may still show the old public data for up to 60 seconds (`max-age=60`). This is accepted.
- **BR-A-22: Admin data exposure.**
  - Admin responses use separate admin DTOs (ADR-0007) and `Cache-Control: no-store`.
  - Admin DTOs expose image **URLs**, never `BlobPath`. The OpenAPI contract guard therefore keeps the `blobPath` rule for all paths and only scopes the quantity rule to public paths (phase plan note before P4-01).
  - Admin user DTOs never contain password hashes, security stamps or tokens.

### 5.3 Images

- **BR-A-23: Accepted types.** JPEG, PNG and WebP only. The type is detected from the file signature (JPEG `FF D8 FF`, PNG `89 50 4E 47`, WebP `RIFF....WEBP`) and must match the declared content type. SVG, GIF, HEIC, AVIF and anything else return 415 `unsupported_image_type`. A signature that does not match the declared type also returns 415 `unsupported_image_type`.
  - **Implemented rule (P4-06, Manager decision 2026-11-07):** PNG detection checks only the 4-byte signature `89 50 4E 47`; the remaining bytes of the 8-byte PNG signature are not checked. JPEG and WebP detection are as stated above.
- **BR-A-24: Size.** One file per request, at most **5 MB** (5,242,880 bytes). Larger returns 413. An empty or missing file returns 400. Nothing is stored on any failure.
  - **Decided (P4-06, Manager 2026-11-07):** the server request-body limit for image uploads is 5 MB plus 64 KB multipart overhead (5,308,416 bytes). A file over 5,242,880 bytes in a request inside that limit is rejected by the upload service with 413 `image_too_large`. A request body over 5,308,416 bytes is rejected by the server before the service runs, with 413 ProblemDetails **without** a `code`. Clients must therefore decide on the HTTP status 413, not on the code.
  - **Decided (P4-06, Manager 2026-11-07):** if Azure Storage (Azurite in development) is unreachable during an upload, the response is 503 `storage_unavailable` and nothing is stored (§7).
- **BR-A-25: Count.**
  - A product has at most **6** images (D-A2, decided by the user 2026-11-06). A 7th upload returns 422 `image_limit_reached`. The limit is a single named constant in the Application layer.
  - A brand has at most one logo, a category one image, and a banner one image. Uploading a new one **replaces** the old one: the new blob is written, the row is updated, and then the old blob is deleted (best effort, logged).
  - The upload locks the product row before counting (for example by updating the product's `UpdatedAtUtc` first in the same transaction), so concurrent uploads cannot exceed the limit.
- **BR-A-26: Storage location.**
  - All images go to the `catalog-images` container in Azure Blob Storage (Azurite in development), through `IImageStorage`.
  - Paths are server-generated: `products/{productId}/{guid}.{ext}`, `brands/{brandId}/{guid}.{ext}`, `categories/{categoryId}/{guid}.{ext}`, `banners/{bannerId}/{guid}.{ext}`. The extension comes from the detected type.
  - The client file name is stored as `OriginalFileName` (truncated to 255 characters) for display only. It is never used in a path, a blob name or a response header.
  - Blobs are written with the detected `Content-Type` and `Cache-Control: public, max-age=31536000, immutable`, and never overwritten.
  - Order of operations and compensation as architecture §7.4: blob first, then row; if the row insert fails, the blob is deleted.
- **BR-A-27: Public visibility (Q-13, approved 2026-11-06).**
  - Blobs in `catalog-images` are anonymously readable by URL, with listing disabled. No other container is public.
  - An image is readable by its unguessable URL as soon as it is uploaded, **including images of Draft products**. The storefront does not link to them until the product is visible.
  - After an image is deleted, browsers (and any CDN added later) may keep serving it until the cache expires, because of `immutable`.
  - **Decided (D-A4, user 2026-11-06):** public read applies to product images, brand logos, category images and banner images. All four types are stored in the same public-read `catalog-images` container. The same applies to them: readable by URL from upload, including for inactive brands, categories and banners.
- **BR-A-28: Primary image and order.**
  - The first image uploaded for a product becomes primary automatically. Setting another image as primary clears the previous one in the same transaction (the filtered unique index enforces at most one).
  - Deleting the primary image promotes the image with the lowest `sortOrder`, then lowest `id`.
  - A new image is appended at the end (`sortOrder` = current maximum + 1).
- **BR-A-29: Alt text** is optional, at most 250 characters, plain text. The storefront falls back to the product name.
- **BR-A-30: Dimensions.** Width and height are recorded when they can be read cheaply from the header, otherwise null. No minimum or maximum dimensions are enforced. Images are not re-encoded, and embedded metadata (for example EXIF location) is **not** removed (see risk R-A-02).

### 5.4 Audit

- **BR-A-31: Audit.**
  - Every successful admin write sets `UpdatedAtUtc` and `UpdatedBy` (the Identity user ID as text) on the changed row; images set `CreatedAtUtc` and `CreatedBy`.
  - Structured log events are written for: sign-in success, sign-in failure, lockout, sign-out, password-reset request and confirmation, password change, admin user create, deactivate, reactivate, unlock and temporary-password set, and every catalogue create, update, delete, publish, archive and image upload or delete. Each event records the action, the entity type and ID, the acting user ID, the client IP and the trace ID.
  - Logs never contain passwords, cookies, antiforgery or reset tokens. A failed sign-in for an unknown email logs no email address.
  - **This rule has no exception, in any environment, including Development** (OP-1, user decision 2026-11-07, option B). No email sender writes a reset link or reset token to the log. The Development `LoggingEmailSender` logs only that a message was suppressed, never the link or the token. Automated tests, including the password-reset flow integration tests (P4-25), obtain the reset link from a test-only capturing email sender in the integration test host, never from the log (architecture §7.3, ADR-0010 §8). No backend channel exposes reset links to Playwright or any other external client.
  - **Outside Development, before the production sender exists (P4-05a)** (OP-2, user decision 2026-11-07, option A): a password-reset request returns 202 as usual, sends no email, and logs only the fact that no email was sent. That log entry contains no token, no email address and no link.
  - **Decided (D-A3, user 2026-11-06):** the row audit fields (`UpdatedBy`, `UpdatedAtUtc`, and `CreatedBy`, `CreatedAtUtc` on images) plus application logs are sufficient. There is no persistent audit history table; the `AuditLog` table stays out of scope (database model §7). Audit history is therefore limited to the application log retention period.

## 6. API Contracts (admin, `/api/v1/admin`)

### 6.1 Common rules

- **Authentication and authorization:** `AdminOnly` (role `Catalog.Admin`) on every endpoint except the BR-A-05 allowlist. 401 when unauthenticated, 403 when authenticated without the role (`0001` AC-A1, AC-A2).
- **Antiforgery:** required on every POST, PUT, PATCH and DELETE (BR-A-04).
- **IDs:** numeric in admin routes (ADR-0007). An unknown ID returns 404 ProblemDetails.
- **Errors:** ProblemDetails per ADR-0011 and architecture §6.2. Codes introduced here: `duplicate_name`, `duplicate_volume`, `duplicate_email`, `in_use`, `publish_requirements_not_met`, `product_was_published`, `published_product_requirement`, `image_limit_reached`, `cannot_deactivate_self`, `last_active_admin`.
- **Image upload codes and statuses** (every endpoint that calls the P4-06 upload service: product images, brand logo, category image, banner create and banner image):

  | Status | `code` | When |
  |---|---|---|
  | 413 | `image_too_large` | File over 5,242,880 bytes in a request within the server body limit (BR-A-24; decided 2026-11-07) |
  | 413 | none | Request body over 5,308,416 bytes (5 MB + 64 KB), rejected by the server before the service runs (BR-A-24) |
  | 415 | `unsupported_image_type` | Type not allowed or signature mismatch (BR-A-23) |
  | 422 | `image_limit_reached` | 7th product image (BR-A-25) |
  | 503 | `storage_unavailable` | Blob storage unreachable during upload; nothing stored (BR-A-24, §7; decided 2026-11-07) |
- **Responses:** create returns 201 with `Location` and the DTO; update and state actions return 200 with the DTO; delete returns 204. All admin responses carry `Cache-Control: no-store`.
- **Lists:** `PagedResult<T>` (ADR-0008) with `page` (>= 1) and `pageSize` (1–100, default 25) where marked "paged"; small lookups return plain arrays.

Error status legend used below: **V** = 400 validation, **A** = 400 `antiforgery_invalid`, **U** = 401, **F** = 403, **N** = 404, **C** = 409, **B** = 422.

### 6.2 Auth and current admin

| Method & route | Auth | Request | Success | Errors |
|---|---|---|---|---|
| `GET /auth/antiforgery` | Anonymous | — | 204, sets `__Host-utp-xsrf` | — |
| `POST /auth/sign-in` | Anonymous | `{ email, password }` | 200 `AdminMe`, sets session cookie | V, A, U, 429 |
| `POST /auth/sign-out` | Anonymous (idempotent) | — | 204; with a valid session also refreshes the security stamp (BR-A-03, D-A5) | A |
| `POST /auth/password-reset/request` | Anonymous | `{ email }` | 202 (always) | V, A, 429 |
| `POST /auth/password-reset/confirm` | Anonymous | `{ email, token, newPassword }` | 204 | V (invalid or expired token, password policy), A |
| `POST /auth/change-password` | AdminOnly | `{ currentPassword, newPassword }` | 204 | V, A, U, F |
| `GET /me` | AdminOnly | — | 200 `AdminMe` | U, F |

`AdminMe { id, email, displayName, roles: string[] }`

**Decided (review L-2, 2026-11-07):** `GET /auth/antiforgery` is **not** rate-limited and lists no 429. The sign-in rate limit (`RateLimiting:AuthPerMinute`, BR-A-01) applies only to `POST /auth/sign-in` and `POST /auth/password-reset/request`, and is not shared with the token endpoint. The OpenAPI contract for `GET /auth/antiforgery` therefore declares no 429 response.

### 6.3 Admin users

| Method & route | Request | Success | Errors |
|---|---|---|---|
| `GET /users` (paged, `q`, `isActive`) | — | 200 `PagedResult<AdminUserDto>` | V, U, F |
| `POST /users` | `{ email, displayName, temporaryPassword }` | 201 `AdminUserDto` | V, A, U, F, C |
| `POST /users/{id}/deactivate` | — | 200 | A, U, F, N, B |
| `POST /users/{id}/reactivate` | — | 200 | A, U, F, N |
| `POST /users/{id}/unlock` | — | 200 | A, U, F, N |
| `POST /users/{id}/temporary-password` | `{ temporaryPassword }` | 204 | V, A, U, F, N |

`AdminUserDto { id, email, displayName, isActive, isLockedOut, lockoutEndUtc|null, createdAtUtc }`. User actions are idempotent, so they carry no row version.

### 6.4 Brands and categories

| Method & route | Request | Success | Errors |
|---|---|---|---|
| `GET /brands` (paged, `q`, `isActive`) | — | 200 `PagedResult<AdminBrand>` | V, U, F |
| `GET /brands/{id}` | — | 200 `AdminBrand` | U, F, N |
| `POST /brands` | `BrandWrite` | 201 `AdminBrand` | V, A, U, F, C |
| `PUT /brands/{id}` | `BrandWrite` + `rowVersion` | 200 `AdminBrand` | V, A, U, F, N, C |
| `DELETE /brands/{id}` | — | 204 | A, U, F, N, B (`in_use`) |
| `PUT /brands/{id}/logo` | multipart `file` | 200 `AdminBrand` | V, A, U, F, N, 413, 415 |
| `DELETE /brands/{id}/logo` | — | 204 | A, U, F, N |

- `BrandWrite { name, slug?, description?, isFeatured, displayOrder, isActive }`
- `AdminBrand { id, name, slug, description, logoUrl|null, isFeatured, displayOrder, isActive, productCount, createdAtUtc, updatedAtUtc, updatedBy, rowVersion }`
- Categories use the same routes under `/categories` and `/categories/{id}/image`, with `CategoryWrite { name, slug?, description?, displayOrder, isActive }` and `AdminCategory` (no `isFeatured`; `imageUrl` instead of `logoUrl`).

### 6.5 Products, variants, notes, images

| Method & route | Request | Success | Errors |
|---|---|---|---|
| `GET /products` (paged, `q` name or SKU, `status`, `brandId`, `sort` = `updated_desc` (default) \| `name_asc`) | — | 200 `PagedResult<AdminProductSummary>` | V, U, F |
| `GET /products/{id}` | — | 200 `AdminProductDetail` | U, F, N |
| `POST /products` | `ProductWrite` | 201 `AdminProductDetail` (Draft) | V, A, U, F, C |
| `PUT /products/{id}` | `ProductWrite` + `rowVersion` | 200 `AdminProductDetail` | V, A, U, F, N, C |
| `POST /products/{id}/publish` | `{ rowVersion }` | 200 `AdminProductDetail` | V, A, U, F, N, C, B |
| `POST /products/{id}/archive` | `{ rowVersion }` | 200 `AdminProductDetail` | V, A, U, F, N, C |
| `DELETE /products/{id}` | — | 204 | A, U, F, N, B (`product_was_published`) |
| `POST /products/{id}/variants` | `VariantWrite` | 201 `AdminVariant` | V, A, U, F, N, C |
| `PUT /products/{id}/variants/{variantId}` | `VariantWrite` + `rowVersion` | 200 `AdminVariant` | V, A, U, F, N, C, B |
| `PUT /products/{id}/notes` | `{ top: int[], heart: int[], base: int[] }` | 200 `AdminProductDetail` | V, A, U, F, N |
| `POST /products/{id}/images` | multipart `file`, `altText?`, `isPrimary?` | 201 `AdminImage` | V, A, U, F, N, 413 (`image_too_large`, or no code over the server body limit), 415, B, 503 (`storage_unavailable`) |
| `PUT /products/{id}/images/{imageId}` | `{ altText?, sortOrder, isPrimary }` | 200 `AdminImage` | V, A, U, F, N |
| `DELETE /products/{id}/images/{imageId}` | — (no `rowVersion`; last-write-wins, BR-A-10) | 204 | A, U, F, N (also for a repeated delete of the same image), B |
| `GET /concentrations` | — | 200 `{ id, code, name, isActive }[]` | U, F |
| `GET /fragrance-notes` (`q`) | — | 200 `{ id, name, slug, productCount }[]` | V, U, F |
| `POST /fragrance-notes` | `{ name, slug? }` | 201 | V, A, U, F, C |
| `PUT /fragrance-notes/{id}` | `{ name, slug? }` | 200 | V, A, U, F, N, C |
| `DELETE /fragrance-notes/{id}` | — | 204 | A, U, F, N, B (`in_use`) |

- `ProductWrite { brandId, concentrationId, name, slug?, shortDescription?, description?, isFeatured, isBestSeller, metaTitle?, metaDescription?, categoryIds: int[] }`. Status is changed only by `publish` and `archive`.
- `VariantWrite { sku, volumeMl, price, originalPrice?, stockQuantity, isActive }`. Prices are integer VND.
- `AdminVariant { id, sku, volumeMl, price, originalPrice|null, stockQuantity, availability, isActive, updatedAtUtc, updatedBy, rowVersion }`
- `AdminImage { id, url, altText|null, isPrimary, sortOrder, contentType, sizeBytes, width|null, height|null, originalFileName|null, createdAtUtc, createdBy }`
- `AdminProductSummary { id, name, slug, brand: { id, name }, concentrationCode, status, primaryImageUrl|null, activeVariantCount, fromPrice|null, totalStock, updatedAtUtc }`
- `AdminProductDetail { id, name, slug, brandId, concentrationId, shortDescription, description, status, publishedAtUtc|null, isFeatured, isBestSeller, metaTitle, metaDescription, categoryIds, notes: { top, heart, base: { fragranceNoteId, name }[] }, variants: AdminVariant[], images: AdminImage[], createdAtUtc, updatedAtUtc, updatedBy, rowVersion }`

### 6.6 Banners

| Method & route | Request | Success | Errors |
|---|---|---|---|
| `GET /banners` | — | 200 `AdminBanner[]` | U, F |
| `GET /banners/{id}` | — | 200 `AdminBanner` | U, F, N |
| `POST /banners` | multipart: `file` + `title`, `subtitle?`, `altText`, `linkUrl?`, `sortOrder`, `isActive`, `startsAtUtc?`, `endsAtUtc?` | 201 `AdminBanner` | V, A, U, F, 413, 415 |
| `PUT /banners/{id}` | JSON fields as above + `rowVersion` (no file) | 200 `AdminBanner` | V, A, U, F, N, C |
| `PUT /banners/{id}/image` | multipart `file` | 200 `AdminBanner` | V, A, U, F, N, 413, 415 |
| `DELETE /banners/{id}` | — | 204 | A, U, F, N |

A banner is created with its image in one request because `ImageBlobPath` is NOT NULL (database model §3.10).

## 7. Edge Cases

| Case | Expected behaviour |
|---|---|
| Two admins edit the same product | The second save returns 409 `concurrency_conflict`; the UI reloads and shows the current data. |
| Double click on "create" | Two requests; the second returns 409 `duplicate_slug` (or `duplicate_sku`). No duplicate rows. |
| Publish of a product whose brand is inactive | 422 `publish_requirements_not_met` listing `brand_inactive`. |
| Brand deactivated while its products are Published | Allowed; the products disappear publicly (`0001` BR-01) and come back when the brand is reactivated. |
| Upload with a `.jpg` name but PNG bytes and declared `image/png` | Accepted as PNG; stored with `.png` extension; the name is only display data. |
| Upload with declared `image/jpeg` but PNG bytes | 415; nothing stored. |
| Upload of exactly 5,242,880 bytes / one byte more | Accepted / 413 `image_too_large`. |
| Request body over 5,308,416 bytes (5 MB + 64 KB) | 413 ProblemDetails without a `code`, rejected by the server before the upload service runs (BR-A-24). |
| Repeated delete of the same product image | 404 (BR-A-10; no `rowVersion`). |
| Blob upload succeeds, database insert fails | The blob is deleted (best effort, logged); the client gets 500 or the mapped error. |
| Row deleted, blob delete fails | 204 to the client; the failure is logged; orphan cleanup is deferred (ADR-0014). |
| Azure Storage or Azurite unreachable during upload | 503 ProblemDetails with `code=storage_unavailable`; nothing is stored. Other admin functions keep working. |
| Session expires while the admin edits a form | Save returns 401; the UI redirects to sign-in. Unsaved changes are lost (accepted; no draft autosave). |
| Antiforgery token from before sign-in used after sign-in | 400 `antiforgery_invalid`; the SPA fetches a new token and retries once. |
| Admin deactivates a colleague who is currently signed in | The colleague's next call fails within 5 minutes (BR-A-09). |
| Session cookie copied before sign-out and replayed afterwards | Works at most until the next security-stamp revalidation, then 401 (at the latest 5 minutes after sign-out (normal case; see R-A-09 for the fail-open case); BR-A-03, D-A5). |
| Admin signed in on two devices signs out on one | The other session also ends within 5 minutes (normal case; see R-A-09 for the fail-open case) (accepted side effect of D-A5, R-A-09). |
| Last active admin is deactivated | 422 `last_active_admin`. |
| Unknown `categoryIds` or fragrance-note IDs | 400 with the offending field in `errors`. |
| Variant volume changed to one already used in the product | 409 `duplicate_volume`. |
| `originalPrice` equal to or below `price` | 400 (BR-07 in `0001`). |

## 8. Acceptance Criteria (testable)

`0001` AC-A1 to AC-A10 remain in force and are not repeated here.

### Sign-in, session and boundary

- **AC-A-01:** Given an active admin with valid credentials and a valid antiforgery token, when they sign in, then they receive 200 with `AdminMe`, and a `__Host-utp-admin` cookie with `HttpOnly`, `Secure`, `SameSite=Strict` and `Path=/` is set.
- **AC-A-02:** Given a **deactivated** admin with the correct password, when they sign in, then they receive the same 401 ProblemDetails as for a wrong password.
- **AC-A-03:** Given a signed-in admin, when they call `POST /auth/sign-out` with a valid token, then they receive 204 and the next `GET /me` with the same cookie returns 401.
  - "The same cookie" includes a **copy** of the session cookie taken before sign-out and replayed afterwards. The 401 holds for that copy within the security-stamp revalidation interval: a `GET /me` with the copied cookie made once the 5-minute interval has elapsed (simulated with `TimeProvider`) returns 401 (normal case; see R-A-09 for the fail-open case) (D-A5, BR-A-03). Before the interval elapses the copy may still be accepted; that window is risk R-A-09.
  - Given the same admin also holds a second session, then after the interval a `GET /me` with the second session's cookie also returns 401 (normal case; see R-A-09 for the fail-open case) (accepted side effect of D-A5).
- **AC-A-04:** Given no session, when `POST /auth/sign-out` is called with a valid token, then 204.
- **AC-A-05:** Given the OpenAPI endpoint list, then every `/api/v1/admin/*` endpoint requires `AdminOnly`, except exactly the five endpoints of BR-A-05. `POST /auth/change-password` without a session returns 401.
- **AC-A-06:** Given a signed-in admin, when 30 minutes pass without a request (simulated with `TimeProvider`), then the next admin call returns 401.

### Admin users

- **AC-A-07:** Given an admin, when they create a user with a unique email and a policy-compliant temporary password, then 201, and the new user can sign in and holds `Catalog.Admin`.
- **AC-A-08:** Given an existing email (in any letter case), when an admin creates a user with it, then 409 `duplicate_email`.
- **AC-A-09:** Given a temporary password of 11 characters, when an admin creates a user, then 400 with an error for `temporaryPassword`.
- **AC-A-10:** Given an admin, when they deactivate their own account, then 422 `cannot_deactivate_self`. Given only one active admin, when anyone tries to deactivate it, then 422 `last_active_admin`.
- **AC-A-11:** Given a locked-out admin, when another admin unlocks them, then the locked admin can sign in with the correct password immediately.
- **AC-A-12:** Given admin B is signed in, when admin A deactivates B or sets B a temporary password, then B's next call after the security-stamp interval returns 401.

### Brands and categories

- **AC-A-13:** Given a brand create without a slug and the name "Maison Lê Đức", then 201 and the slug is `maison-le-duc`.
- **AC-A-14:** Given an existing brand slug, when another brand is created with it, then 409 `duplicate_slug`.
- **AC-A-15:** Given a brand read with `rowVersion` R, when another admin updates it first and the first admin then saves with R, then 409 `concurrency_conflict` and the second update is preserved.
- **AC-A-16:** Given a brand with at least one product, when it is deleted, then 422 `in_use` and the brand still exists. Given a brand with no products, then 204.
- **AC-A-17:** Given an admin deactivates a brand, when the public `GET /api/v1/brands/{slug}` is called afterwards on the same instance, then 404 (output cache evicted).

### Products and variants

- **AC-A-18:** Given a product create, then 201, `status = Draft`, `publishedAtUtc = null`, and the product is not visible publicly.
- **AC-A-19:** Given a Draft product with no image, when it is published, then 422 `publish_requirements_not_met` naming the missing image; the status stays Draft.
- **AC-A-20:** Given a complete Draft product, when it is published, then 200, `status = Published`, `publishedAtUtc` is set, and it appears in the public catalogue.
- **AC-A-21:** Given a product published at T1, archived, and published again at T2, then `publishedAtUtc` is still T1.
- **AC-A-22:** Given a product that was ever published, when it is deleted, then 422 `product_was_published`. Given a never-published Draft product with variants and images, then 204, all its rows are gone, and its blobs are deleted from the fake storage.
- **AC-A-23:** Given a Published product with one active variant, when that variant is set `isActive = false`, then 422 `published_product_requirement`.
- **AC-A-24:** Given an admin sets a variant's `stockQuantity` to 3 with `Catalog:LowStockThreshold=5`, then the admin response shows `stockQuantity = 3` and `availability = LowStock`, and the public product detail shows `LowStock` with no quantity field.
- **AC-A-25:** Given a variant update with `stockQuantity = -1`, a decimal `price`, or `originalPrice <= price`, then 400 with errors keyed by those fields.
- **AC-A-26:** Given SKU `ab-100` on one variant, when another variant is saved with `AB-100`, then 409 `duplicate_sku`.
- **AC-A-27:** Given `PUT /products/{id}/notes` with the same note twice in `top`, then 400. Given a valid body, then the public product detail shows the notes in the submitted order.
- **AC-A-28:** Given a fragrance note used by a product, when it is deleted, then 422 `in_use`.
- **AC-A-29:** Given `GET /admin/concentrations`, then the seeded list is returned, and no POST, PUT or DELETE route exists for concentrations.

### Images

- **AC-A-30:** Given a valid 1 MB WebP upload for a product with no images, then 201, the image is primary, the blob path matches `products/{productId}/{guid}.webp`, the returned `url` starts with `Storage:Images:PublicBaseUrl`, and no admin or public DTO contains `blobPath`.
- **AC-A-31:** Given an upload of an SVG, a GIF, or PNG bytes declared as `image/jpeg`, then 415 `unsupported_image_type` and the fake storage contains no new blob.
- **AC-A-32:** Given an upload of 5,242,881 bytes, then 413 `image_too_large` (code decided 2026-11-07) and nothing is stored. Given exactly 5,242,880 bytes of a valid JPEG, then 201.
- **AC-A-33:** Given a product with 6 images, when another is uploaded, then 422 `image_limit_reached` and nothing is stored. Given a product with 5 images, when one more is uploaded, then 201. Given two concurrent uploads to a product with 5 images, then exactly one succeeds and the product has 6 images.
- **AC-A-34:** Given a product with images A (primary) and B, when B is set primary, then A is no longer primary. When the primary is deleted, then the remaining image with the lowest `sortOrder` becomes primary.
- **AC-A-35:** Given an upload whose original file name is `../../evil.jpg`, then the blob path contains no part of that name, and `originalFileName` is returned only as data.
- **AC-A-36:** Given the fake storage fails the database step after upload, then the uploaded blob is deleted (compensation verified in the fake).
- **AC-A-37:** Given a brand with a logo, when a new logo is uploaded, then the brand's `logoUrl` points to the new blob and the old blob is deleted.

### Antiforgery and audit

- **AC-A-38:** Given every admin POST, PUT, PATCH and DELETE endpoint (enumerated from the endpoint data source, including multipart uploads), when it is called by a signed-in admin without `X-XSRF-TOKEN`, then 400 `antiforgery_invalid` and the database and fake storage are unchanged.
- **AC-A-39:** Given an admin updates a product, then `updatedBy` equals that admin's user ID and `updatedAtUtc` equals the test clock time, and a structured log event with action, entity, ID, user ID and trace ID is written (captured by a test logger).
- **AC-A-40:** Given a failed sign-in, then the captured log contains no password and, for an unknown email, no email address.

### Admin UI

- **AC-A-41:** Given an unauthenticated visitor opens `/admin/products`, then the UI redirects to `/admin/login`. After sign-in, it returns to `/admin/products`.
- **AC-A-42:** Given a 409 `concurrency_conflict` on save, then the UI shows a conflict message and offers to reload the record. Given a 400, then field errors are shown next to the fields.
- **AC-A-43:** Given the product form, then stock quantity is editable per variant, prices accept whole dong only, and image upload rejects files over 5 MB or of other types before sending (usability only; the server still enforces BR-A-23 and BR-A-24).

## 9. Decisions (resolved by the user, 2026-11-06)

All decision points of the draft are resolved. Two later points, OP-1 and OP-2, were raised after the P4-05 split and were decided by the user on 2026-11-07 (rows below and §12). Two more, D-A5 and D-A6, came from the P4-02 review and were decided by the user on 2026-11-07. They are numbered D-A5 and D-A6 to avoid confusion with the phase-plan decisions D-1 and D-2 (`docs/tasks/0001-phase-plan.md`).

| ID | Question | Decision | Effect |
|---|---|---|---|
| D-A1 | One equal-rights `Catalog.Admin` role, or a separate role for admin user management? | **One Admin role for all admin functions.** | BR-A-06 as written; no extra role or policy |
| D-A2 | Maximum images per product? | **6** (replaces the draft default of 10). | BR-A-25, AC-A-33; P4-06 |
| D-A3 | Row audit fields plus logs, or a persistent audit history table? | **No permanent audit table.** `UpdatedBy` / `UpdatedAtUtc` fields and application logs are sufficient. | BR-A-31; no migration; `AuditLog` stays out of scope |
| D-A4 | Does public read also cover brand logos, category images and banner images? | **Yes.** They sit in the same public-read `catalog-images` container as product images. | BR-A-27; `0001` §8.1a; ADR-0014 §5 |
| Q-12 | Password-reset email provider and sender identity | **Decided:** Gmail, sending from the provider account the user named, with the sender address the user named (neither address is recorded in the project documents; see `0001` Q-12 and ADR-0010 §8). The Gmail app password (or equivalent credential) is a secret held only in the deployment secret store. | P4-05a (production sender); risk R-A-06 |
| Q-14 | Enforce two-factor sign-in (TOTP) for admins before go-live? | **Not required before go-live.** | Risk R-A-07 |
| OP-1 | May the Development `LoggingEmailSender` write the token-bearing reset link to the log? | **Decided 2026-11-07, option B: no.** Reset tokens and reset links are never logged, in any environment; BR-A-31 has no exception. Automated tests obtain the reset link through a test-only capturing email sender (since the P4-25 revision of 2026-11-07, option A: integration tests only, no Playwright). | BR-A-31; architecture §7.3; ADR-0010 §8; P4-05, P4-25 |
| OP-2 | Behaviour of a reset request outside Development before P4-05a exists? | **Decided 2026-11-07, option A:** 202 as usual, no email sent, and only the fact that no email was sent is logged (no token, no address, no link). | BR-A-31; architecture §7.3; ADR-0010 §8; P4-05 |
| D-A5 (2026-11-07) | Should sign-out make a copied session cookie stop working? | **Decided 2026-11-07, option (b): yes.** Sign-out with a valid session refreshes the user's security stamp, so a copied cookie stops working within the 5-minute revalidation interval (normal case; see R-A-09 for the fail-open case). Accepted side effect: all other sessions of that user end within the same interval. | BR-A-03, BR-A-09, AC-A-03; architecture §7.1; ADR-0010 §2; risk R-A-09; P4-02 |
| D-A6 (2026-11-07) | Is `Bootstrap:AllowNonDevelopment` the approval switch for running `bootstrap-admin` outside Development? | **Decided 2026-11-07: yes.** Setting it to `true` is the explicit user-approval gate. It and any temporary `Bootstrap:AdminPassword` are removed straight after use. | ADR-0010 §6; architecture §7.1; `docs/dev-setup.md` §8; P4-02 |

## 10. Assumptions

- AS-A-01: The admin UI uses the same language as the storefront. No separate localisation.
- AS-A-02: The number of admins is small (fewer than 20), so the user list needs no advanced search.
- AS-A-03: Admins use a current desktop browser. The admin UI is usable on a tablet but is not optimised for phones.
- AS-A-04: Image upload volume is low (ADR-0014), so uploading through the API is sufficient.
- AS-A-05: Stock is maintained manually by admins until orders exist (ADR-0005).

## 11. Risks

- **R-A-01:** Images (product images, brand logos, category images and banner images) are public by URL from the moment of upload, including for Draft products and inactive brands, categories and banners (BR-A-27). Mitigation: unguessable GUID names, listing disabled, admin guidance.
- **R-A-02:** Uploaded photos may carry EXIF metadata (for example GPS location) that becomes public. Stripping it requires re-encoding and a new image-processing dependency, which is out of scope. Mitigation: admin guidance; revisit with a separate decision if needed.
- **R-A-03:** Deleted images may be served from browser caches (and a future CDN) for up to one year because of `immutable`. Mitigation: names are never reused; the storefront stops linking at once.
- **R-A-04:** Orphan blobs accumulate when compensation or deletion fails. Mitigation: logged; cleanup job deferred (ADR-0014).
- **R-A-05:** Output-cache eviction is per instance. With scale-out, other instances can show stale public data for up to 60 seconds. Accepted.
- **R-A-06 (accepted user decision, 2026-11-06; Q-12):** Password-reset email is sent through a personal Gmail account.
  - Gmail sending limits apply; reset emails can be delayed or refused when they are reached.
  - A personal Gmail account is a non-enterprise production dependency: no service-level agreement, tied to one person's account, and subject to Google account security and policy changes.
  - Gmail may refuse or rewrite a sender address that is not the authenticated account or a verified alias of it (see the open point in `0001` Q-12).
  - The Gmail app password (or equivalent credential) is a secret. It is stored only in the deployment secret store and never in the repository, configuration files or documents.
  - Mitigation: admin-assisted reset (BR-A-07) stays available. **To be reviewed before go-live.**
- **R-A-07 (accepted user decision, 2026-11-06; Q-14):** Admin sign-in has no second factor. A stolen or guessed admin password gives full catalogue and admin-user control. Mitigation: password policy, lockout, sign-in rate limit, `SameSite=Strict` cookie, security-stamp revocation, sign-in audit logs. TOTP remains available in Identity and can be enabled later without a schema change.
- **R-A-08 (accepted user decision, 2026-11-06; D-A3):** Without a persistent audit table, change history older than the application log retention period cannot be reconstructed, and only the last editor of a row is stored.
- **R-A-09 (accepted user decision, 2026-11-07; D-A5):** Sign-out refreshes the security stamp, but the stamp is only revalidated every 5 minutes.
  - A session cookie copied before sign-out can still be used for **up to 5 minutes** after sign-out.
  - Sign-out also ends every other session of the same user within those 5 minutes. An admin signed in on two devices who signs out on one is signed out on both.
  - Mitigation: `HttpOnly`, `Secure` and `SameSite=Strict` cookie; 30-minute idle and 8-hour absolute limits; sign-out audit log. Shortening the interval (more frequent database reads) is not part of D-A5 and would need its own decision.
  - **Fail-open on stamp refresh failure (review L-A):** if `UpdateSecurityStampAsync` fails during sign-out (database outage or concurrency failure), the endpoint still clears the browser cookie and returns 204. A copied cookie and the user's other sessions then stay valid until the 30-minute sliding idle timeout or the 8-hour absolute limit, not the 5-minute window. The failure is logged as event 5103 at Warning with the user ID, client IP and trace ID only (no email, stamp or exception message). Recommendation for operations (Phase 5): alert on event 5103.

## 12. Task Split for P4-08+ (written into the phase plan)

The task split is now in `docs/tasks/0001-phase-plan.md`, Phase 4 (added 2026-11-07 after approval). P4-01 to P4-07 keep their numbers; they were annotated only with the BR-A and AC-A IDs they cover, and P4-06 now depends on P4-08 and delivers the reusable upload service plus the product image upload and delete endpoints. The new tasks are P4-08 to P4-25: admin write foundation; admin users API; brands API; categories API; products lifecycle API; variants API; fragrance notes API; product image metadata API; banners API; admin UI foundation; admin UI for users, brands and categories, products, product editor sections, notes and banners, and password pages; admin E2E journeys; password-reset flow integration tests (P4-25, see below). Each row in the phase plan lists the BR-A and AC-A IDs it covers.

P4-05 was split on 2026-11-07 with the user's approval. Only **P4-05a** (the production Gmail sender) is **blocked** until the user confirms the sender setup (the open point in `0001` Q-12 and ADR-0010 §8). P4-05 (password reset and change password endpoints and the email abstraction), P4-23 and P4-25 are **not blocked**; they use the Development and test senders only and do not depend on P4-05a.

Points decided by the user on 2026-11-07 (they affect P4-05 and P4-25; see §9):

- **OP-1, decided (option B):** reset tokens and reset links are never written to logs, in any environment; BR-A-31 holds without exception. The Development `LoggingEmailSender` does not log the reset link or the token. P4-25 obtains the reset link through a test-only capturing email sender, never from the log; that sender is registered only in the integration test host (decision 2026-11-07), never in Development or any other environment, and a P4-05 test proves it is absent from a non-test host. Applying BR-A-31 (decision 2026-11-07, not a new requirement), the OP-1 log test covers both the request-and-confirm flow and the change-password flow: the logs of those flows contain no reset token, no reset link, no password and no password hash. Consequence: in Development a developer cannot read a reset link from the log or from a capturing sender; manual testing in Development uses admin-assisted reset (BR-A-07).
- **Test ownership (decision 2026-11-07):** each test is written once. P4-05 owns the test-only capturing sender; the OP-1 log test, an integration test that the logs of a full request-and-confirm flow and of the change-password flow contain no reset token, reset link, password or password hash; and the absence test, which starts a non-test host and asserts that the capturing sender is not registered (moved from P4-25 to P4-05 on 2026-11-07, because P4-05 delivers the sender). P4-25 reuses all three and does not write its own copy of the log test or the absence test.
- **OP-2, decided (option A):** outside Development, until P4-05a is delivered, `POST /admin/auth/password-reset/request` returns 202 as usual, sends no email, and logs only the fact that no email was sent (no token, no email address, no link). Production reset stays admin-assisted (BR-A-07) until P4-05a is done. P4-05a stays blocked on the sender setup, unchanged.

**P4-25 revised 2026-11-07 (user decision, option A):** the Playwright password-reset E2E is removed. P4-25 is now a set of integration tests in `tests/UTperfume.IntegrationTests` covering the full reset flow: request returns 202 for known and unknown addresses; the test-only capturing sender (delivered by P4-05) captures the link in the integration test host; confirm returns 204 for a valid token, after which sign-in with the new password succeeds and the old password fails; confirm returns 400 for an expired or reused token (the Developer chooses the test clock or a shortened token lifespan in the test host, after checking that Identity's token provider honours the clock used); change password returns 204 for an authenticated admin and 401 without a session. The OP-1 log test (no reset token, reset link, password or password hash in the logs of the request-and-confirm and change-password flows) and the absence test (a non-test host does not register the capturing sender) are owned by P4-05; P4-25 covers both only by reference. It covers BR-A-04, BR-A-05, BR-A-09 and BR-A-31, AC-A-05 and AC-A-38, and `0001` AC-A9. No backend channel exposes reset links to Playwright. P4-24 (admin E2E journeys) keeps only the admin pages that do not need an emailed link. P4-23 is tested with Vitest and React Testing Library and does not depend on P4-25.
