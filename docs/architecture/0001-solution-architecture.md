# 0001 – Solution Architecture

- Status: **Accepted** (user approval 2026-11-05). Sub-items still open are marked *Proposed* inline (Q-07 to Q-11). Q-12, Q-13, Q-14, D-12 and the admin decisions D-A1 to D-A4 (`docs/requirements/0002-admin-requirements.md` §9) decided 2026-11-06.
- Date: 2026-11-05 (revised after the user's decisions; revised 2026-11-06)
- Requirements: `docs/requirements/0001-perfume-catalogue-requirements.md`
- Database detail: `docs/architecture/0002-database-model.md`. It is a separate file because the model is large and changes through migrations; §8 summarises it.
- Decisions: `docs/decisions/0001`–`0014`

## 1. Context

This is a greenfield repository. It contains `CLAUDE.md`, `global.json` (SDK 10.0.401), the agent definitions and `docs/`. Creating the git repository and the GitHub remote is approved.

The approved stack is fixed by `CLAUDE.md`:

- **Backend:** ASP.NET Core Web API (.NET 10, C# 14), EF Core 10 with SQL Server / Azure SQL, OpenAPI, xUnit, FluentAssertions **7.x**.
- **Frontend:** React, TypeScript, Vite, Vitest, React Testing Library, Playwright.
- **Infrastructure:** GitHub Actions, Azure App Service, Azure SQL.
- **Added by user approval (2026-11-05):** Azure Blob Storage for product images (ADR-0014).

## 2. High-Level View

```
Browser (React SPA)
   │  same origin: https://<host>/
   ├── /, /perfumes, /perfumes/{slug}, /brands/... ─► static SPA (index.html fallback)
   ├── /api/v1/...        ─────────────────────────► ASP.NET Core API ──EF Core 10──► Azure SQL (schemas "catalog", "identity")
   │                                                   │ Managed Identity
   │                                                   └── upload/delete ──────────► Azure Blob Storage (container "catalog-images")
   └── <img src="https://<account>.blob.core.windows.net/catalog-images/...">  (public read, ADR-0014)
```

- A single Azure App Service hosts the API and serves the built SPA from `wwwroot`.
- The SPA and API share an origin, so there is no CORS, and the admin session cookie can be `SameSite=Strict` (ADR-0010, ADR-0012).
- Requests for `/api/*` never fall back to `index.html`.

## 3. Repository and Folder Structure

```
/
  .config/dotnet-tools.json         dotnet-ef (local tool)
  .editorconfig .gitattributes .gitignore README.md
  Directory.Build.props             net10.0, LangVersion 14, Nullable, ImplicitUsings, TreatWarningsAsErrors, analyzers
  Directory.Packages.props          Central Package Management (FluentAssertions pinned [7.0.0,8.0.0))
  global.json                       (exists)
  UTperfume.slnx
  openapi/v1.json                   generated at build, committed (ADR-0009)
  src/
    UTperfume.Api/
    UTperfume.Application/
    UTperfume.Infrastructure/
  tests/
    UTperfume.UnitTests/
    UTperfume.IntegrationTests/
  web/
    e2e/                            Playwright
  docs/{requirements,architecture,decisions,tasks}/
    docs/dev-setup.md               Docker-free local setup: LocalDB, Azurite, certificates (created in P0-05)
  .github/workflows/ci.yml
```

## 4. Backend Structure (ADR-0012)

### 4.1 Projects and dependency direction

```
UTperfume.Api ──► UTperfume.Application ◄── UTperfume.Infrastructure
      └──────────────► UTperfume.Infrastructure   (composition root only: AddInfrastructure())
```

| Project | Contains | Must not reference |
|---|---|---|
| `UTperfume.Application` | Domain entities and enums (`Domain/`), feature handlers, DTOs, business rules (visibility, from-price, availability, slug rules), abstractions `ICatalogDbContext` and `IImageStorage` | ASP.NET Core, EF Core SQL Server provider, Azure SDK |
| `UTperfume.Infrastructure` | `AppDbContext : IdentityDbContext<AdminUser,…>, ICatalogDbContext`, entity configurations, `Migrations/`, `Identity/AdminUser.cs`, `Storage/AzureBlobImageStorage.cs`, email sender implementations, development seeder | Api |
| `UTperfume.Api` | `Program.cs`, endpoint groups, Identity/cookie/antiforgery setup, ProblemDetails, OpenAPI, rate limiting, output cache, health checks, SPA fallback, sitemap, `bootstrap-admin` command | — |

- There is **one DbContext with one migration history**. Catalog tables are in schema `catalog` and Identity tables in schema `identity`. This is simpler than two contexts, and the migrations stay ordered.
- There is **no separate Domain project** and **no repository layer**. Handlers query through `ICatalogDbContext` with `AsNoTracking` projections.
- `Application` references only the base `Microsoft.EntityFrameworkCore` package (ADR-0012).
- `TimeProvider` is injected, so banner windows and the "new" badge are deterministic in tests.
- **Shared rules:** `CatalogVisibility.VisibleProducts()` (BR-01) and `Availability.From(quantity, threshold)` (BR-08) are single implementations reused by every query.

### 4.2 Feature folders

```
UTperfume.Application/
  Domain/            Brand Category Concentration Product ProductVariant ProductImage FragranceNote
                     ProductNote ProductCategory HomeBanner ProductStatus NoteType Availability
  Common/            PagedResult<T>  Result/Error  Slug  CatalogVisibility  Money (VND helpers)
                     ICatalogDbContext  IImageStorage  CatalogOptions (NewBadgeDays, LowStockThreshold)
  Features/
    Home/ Products/ Brands/ Categories/ CatalogFilters/ Sitemap/
    Admin/           (Phase 4) Auth/ Brands/ Categories/ Products/ Variants/ Images/ Notes/ Banners/
UTperfume.Api/
  Endpoints/         HomeEndpoints ProductEndpoints BrandEndpoints CategoryEndpoints
                     CatalogFilterEndpoints SitemapEndpoints Admin/*.cs
                     (one file per feature so parallel tasks do not share writable files)
  Infrastructure/    ProblemDetails + IExceptionHandler, Result→HTTP mapping, IdentitySetup,
                     AntiforgeryEndpointFilter, RateLimitSetup, SecurityHeaders
  Program.cs
```

### 4.3 Endpoint style

- Minimal APIs with `MapGroup`, `TypedResults`, explicit `.Produces*`, `.WithName()` and `.WithTags()`.
- .NET 10 built-in validation (`AddValidation()`) on query and request records.
- Cross-field checks (`minPrice <= maxPrice`, `sort` values) are done in handlers and return a validation error.

### 4.4 Backend packages

| Package | Project | Reason |
|---|---|---|
| `Microsoft.AspNetCore.OpenApi` | Api | Built-in OpenAPI document |
| `Microsoft.Extensions.ApiDescription.Server` | Api | Emits `openapi/v1.json` at build (ADR-0009) |
| `Microsoft.EntityFrameworkCore` | Application | `IQueryable` async extensions |
| `Microsoft.EntityFrameworkCore.SqlServer`, `.Design` | Infrastructure (Design: PrivateAssets) | Approved stack |
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | Infrastructure | Admin accounts (ADR-0010) |
| `Azure.Storage.Blobs` | Infrastructure | Image storage (ADR-0014, user-approved) |
| `Azure.Identity` | Infrastructure | `DefaultAzureCredential` for Managed Identity to Blob Storage and Azure SQL |
| `Azure.Extensions.AspNetCore.DataProtection.Blobs` | Api (Phase 5, *Proposed*) | Persist Data Protection keys across instances and restarts (§7.3) |
| *(none)* | Api | `/health/ready` uses the custom `DatabaseHealthCheck` (`Database.CanConnectAsync`). It returns 503 when the connection string is missing or the database is unreachable, and it needs no extra package. |
| `xunit.v3`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk` | tests | Approved stack |
| `FluentAssertions` **7.x**, pinned `[7.0.0,8.0.0)` | tests | Approved stack; Apache-2.0 (ADR-0013) |
| `Microsoft.AspNetCore.Mvc.Testing` | IntegrationTests | WebApplicationFactory |

- Cookie authentication, antiforgery, output caching, rate limiting and ProblemDetails are part of the ASP.NET Core framework and need no packages.
- **Not used:** Testcontainers (Docker is unavailable), JwtBearer, MSAL, Respawn.
- Email provider (Q-12, decided 2026-11-06): Gmail over SMTP. An SMTP package is added only if the .NET base library is not used, and then needs justification and approval (P4-05a, the production sender task).

## 5. Frontend Structure

```
web/
  index.html  vite.config.ts (dev proxy /api → API; vitest config)  eslint.config.js  tsconfig*.json
  playwright.config.ts
  public/robots.txt
  src/
    main.tsx
    app/          App.tsx  router.tsx  providers.tsx (QueryClientProvider)  queryClient.ts
                  layouts/PublicLayout.tsx  layouts/AdminLayout.tsx (Phase 4)  ErrorBoundary.tsx  NotFoundPage.tsx
    api/          schema.d.ts (generated – do not edit)
                  client.ts    openapi-fetch instance + middleware (adds X-XSRF-TOKEN on unsafe methods)
                  problem.ts   ProblemDetails → ApiError; unwrap() throws ApiError
                  queries/     keys.ts + one file per resource: home.ts products.ts brands.ts categories.ts filters.ts
                  admin/       (Phase 4) auth.ts + mutations per resource
    features/
      home/ catalogue/ product/ brands/ categories/ admin/ (Phase 4, lazy-loaded)
    shared/
      ui/           ProductCard, Price, AvailabilityBadge (InStock/LowStock/OutOfStock), Spinner
      lib/          formatVnd.ts  routes.ts
      seo/          Seo.tsx
    test/           renderWithProviders.tsx (fresh QueryClient, retry off, MemoryRouter)
  e2e/            home.spec.ts catalogue.spec.ts product.spec.ts
```

### 5.1 Client routes

| Route | Page | API calls |
|---|---|---|
| `/` | Home | `GET /api/v1/home` |
| `/perfumes?q&brand&category&concentration&volume&minPrice&maxPrice&sort&page` | Catalogue | `GET /api/v1/products`, `GET /api/v1/catalog/filters` |
| `/perfumes/:slug` | Product detail | `GET /api/v1/products/{slug}`, `GET /api/v1/products/{slug}/related` |
| `/brands` | Brand list | `GET /api/v1/brands` |
| `/brands/:slug` | Brand page | `GET /api/v1/brands/{slug}` + products with `brand={slug}` |
| `/categories/:slug` | Category page | `GET /api/v1/categories/{slug}` + products with `category={slug}` |
| `/admin/login`, `/admin/*` | Admin (Phase 4) | `/api/v1/admin/*` |
| `*` | Not found | — |

### 5.2 Frontend rules

- **URL state:** filter, sort and page state lives in the URL query string. `useSearchParams` is the single source of truth (AC-S2), and query parameter names match the API.
- **Data access with TanStack Query and openapi-fetch:**
  - Components never call `fetch` or `client` directly. They call hooks in `api/queries`.
  - **Query keys:** each resource has a key factory in `api/queries/keys.ts`, e.g. `productKeys.list(params)` = `['products','list',params]` and `productKeys.detail(slug)`.
  - **Query functions:** hooks call the typed client and unwrap the result, e.g. `useQuery({ queryKey, queryFn: ({ signal }) => unwrap(client.GET('/api/v1/products', { params: { query }, signal })) })`. `unwrap` returns `data` or throws an `ApiError` built from the ProblemDetails, so `error` in TanStack Query is always typed. Passing `signal` cancels superseded requests when filters change.
  - **Catalogue paging:** the catalogue uses `placeholderData: keepPreviousData`, so the grid does not blank between pages or filters.
  - **QueryClient defaults:** `staleTime: 60_000` (matches the server `max-age=60`), `refetchOnWindowFocus: false`, and retry only on network or 5xx errors, at most once. 4xx errors are never retried.
  - **Admin mutations (Phase 4):** use `useMutation`, then `invalidateQueries` on the affected keys. The client middleware attaches the antiforgery header. Cookies are sent automatically on the same origin.
  - No `openapi-react-query` wrapper package. The hand-written hooks are thin, and this avoids an extra dependency.
- **States:** the UI shows loading, empty and error states. On 400 it renders field errors from `ApiError`.
- **Prices:** `formatVnd` uses `Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND', maximumFractionDigits: 0 })`. Price filter inputs accept whole dong only.
- **Availability:** `AvailabilityBadge` shows "In stock", "Low stock" or "Out of stock". It never shows a quantity, because the API does not send one.
- **SEO:** each page renders `<Seo>`, using React 19 native `<title>`/`<meta>` hoisting. Product pages emit JSON-LD `Product` with `offers.priceCurrency = "VND"` and `availability`: `InStock` / `LimitedAvailability` / `OutOfStock`.
- **Admin pages:** a lazy-loaded route tree. UI guards (a redirect to `/admin/login` on 401) are for usability only; the server enforces access.

### 5.3 Frontend packages

| Package | Kind | Reason |
|---|---|---|
| `react-router` | runtime | Routing and URL search-param state |
| `openapi-fetch` | runtime (~6 kB) | Typed client built from the generated schema |
| `@tanstack/react-query` | runtime (**approved**) | Caching, deduplication, cancellation, `keepPreviousData` paging, and mutations with invalidation |
| `openapi-typescript` | dev | Generates `schema.d.ts` |
| `vitest`, `@testing-library/react`, `@testing-library/user-event`, `@testing-library/jest-dom`, `jsdom` | dev | Approved test stack |
| `@playwright/test` | dev | E2E |

## 6. API Design

### 6.1 Conventions

- **Base path and format:** the base path is `/api/v1`. JSON uses camelCase. Enums are strings. Timestamps are UTC ISO 8601.
- **Money:** amounts are **whole VND as JSON integers** (C# `long`, OpenAPI `integer`/`int64`), and `currency` is always `"VND"`.
- **Lookups:** slug lookups are exact and lowercase.
- **Multi-value parameters** are repeated (`brand=dior&brand=chanel`), with at most 20 values each.
- **Public caching:** `Cache-Control: public, max-age=60`, plus a 60 s server output cache varied by query. Admin writes evict output-cache tags.
- **Rate limiting:**
  - Public endpoints: 120 per minute per IP (configurable).
  - Admin sign-in and password-reset request: 5 per minute per IP.
  - Exceeding a limit returns 429 ProblemDetails.
- **Pagination envelope (ADR-0008):** `{ "items": [...], "page": 1, "pageSize": 24, "totalItems": 137, "totalPages": 6 }`

### 6.2 Error model (ADR-0011)

All non-2xx responses from `/api/*` are RFC 9457 `application/problem+json` with `traceId`.

| Status | When | Body |
|---|---|---|
| 400 | Invalid query or body; invalid or missing antiforgery token on an admin write | `ValidationProblemDetails` with `errors` keyed by parameter, or ProblemDetails with `code=antiforgery_invalid` |
| 401 | Admin endpoint unauthenticated; failed sign-in (generic message) | ProblemDetails |
| 403 | Authenticated but not `Catalog.Admin` | ProblemDetails |
| 404 | Unknown, invisible or inactive resource; unknown `/api` route | ProblemDetails (no reason disclosed) |
| 409 | Admin: duplicate slug or SKU, or concurrency mismatch | ProblemDetails with `code` (`duplicate_slug`, `duplicate_sku`, `concurrency_conflict`) |
| 413 | Admin image upload over 5 MB | ProblemDetails |
| 415 | Admin image upload that is not JPEG, PNG or WebP by signature, or whose signature does not match the declared content type | ProblemDetails with `code=unsupported_image_type` |
| 422 | Admin business-rule violation | ProblemDetails with `code` |
| 429 | Rate limited | ProblemDetails, `Retry-After` header |
| 500 | Unhandled | Generic ProblemDetails |

The cookie handler's redirects to a login page are disabled. The API returns 401 or 403 ProblemDetails instead (§7).

### 6.3 Shared response shapes (public)

```text
BrandRef        { slug, name }
CategoryRef     { slug, name }
ConcentrationRef{ code, name }
ImageDto        { url, altText }           // url = Storage:Images:PublicBaseUrl + "/" + BlobPath
Availability    "InStock" | "LowStock" | "OutOfStock"

ProductSummary {
  id: int, slug, name, brand: BrandRef, concentration: ConcentrationRef,
  primaryImage: ImageDto | null,
  fromPrice: int64, fromOriginalPrice: int64 | null, currency: "VND",
  volumesMl: number[],            // active variants, ascending
  inStock: bool,                  // any active variant InStock or LowStock
  isNew: bool
}

ProductDetail {
  id, slug, name, brand, concentration, shortDescription|null, description|null,
  categories: CategoryRef[],
  images: { url, altText, isPrimary, width|null, height|null }[],   // primary first, then sortOrder
  notes: { top: NoteRef[], heart: NoteRef[], base: NoteRef[] },
  variants: VariantDto[],         // active only, ascending volume
  currency: "VND", seo: { metaTitle, metaDescription|null }, publishedAtUtc
}

VariantDto { sku, volumeMl: number, price: int64, originalPrice: int64|null,
             discountPercent: int|null, availability: Availability }
             // NO stockQuantity – admin-only (BR-08, BR-28)

BrandSummary    { slug, name, logoUrl|null, productCount }
BrandDetail     { slug, name, description|null, logoUrl|null, productCount }
CategorySummary { slug, name, imageUrl|null, productCount }
CategoryDetail  { slug, name, description|null, imageUrl|null, productCount }
BannerDto       { title, subtitle|null, imageUrl, altText, linkUrl|null }
```

`id` on the product shapes is included for React keys and future cart use. It is not used in URLs.

### 6.4 Public endpoints (anonymous, `AllowAnonymous`)

| # | Method & route | Query parameters | 200 response | Error statuses |
|---|---|---|---|---|
| E1 | `GET /api/v1/home` | — | `{ banners, featuredProducts, newArrivals, bestSellers, popularBrands, categories }` | 429, 500 |
| E2 | `GET /api/v1/products` | `q` (2–100), `brand[]`, `category[]`, `concentration[]`, `volume[]` (decimal > 0), `minPrice`, `maxPrice` (**integer VND** 0–10,000,000,000; min <= max), `sort` (`newest`\|`price_asc`\|`price_desc`\|`name_asc`\|`name_desc`), `page` (>= 1), `pageSize` (1–60, default 24) | `PagedResult<ProductSummary>` | 400, 429, 500 |
| E3 | `GET /api/v1/products/{slug}` | — | `ProductDetail` | 404, 429, 500 |
| E4 | `GET /api/v1/products/{slug}/related` | `limit` (1–12, default 8) | `ProductSummary[]` | 400, 404, 429, 500 |
| E5 | `GET /api/v1/brands` | — | `BrandSummary[]` | 429, 500 |
| E6 | `GET /api/v1/brands/{slug}` | — | `BrandDetail` | 404, 429, 500 |
| E7 | `GET /api/v1/categories` | — | `CategorySummary[]` | 429, 500 |
| E8 | `GET /api/v1/categories/{slug}` | — | `CategoryDetail` | 404, 429, 500 |
| E9 | `GET /api/v1/catalog/filters` | — | `{ brands, categories, concentrations, volumesMl, priceRange: { min: int64, max: int64 } \| null, currency: "VND", sortOptions }` | 429, 500 |
| E10 | `GET /sitemap.xml` | — | XML sitemap | 500 |
| E11 | `GET /health/live`, `GET /health/ready` | — | Health status (`ready` checks the database) | 503 |

Notes on these endpoints:

- Filter options are at `/catalog/filters` so they cannot collide with `/products/{slug}`.
- The brand and category pages reuse E2.
- Facet counts are not included in this version.

### 6.5 Admin boundary (ADR-0007, ADR-0010)

- **Route groups:**
  - `/api/v1/admin/auth/*`: sign-in and password reset. Anonymous by design and rate-limited; an explicit allowlist in the boundary test.
  - All other `/api/v1/admin/*` routes: `.RequireAuthorization("AdminOnly")`, `Cache-Control: no-store`, no output cache.
  - Every admin route, including `auth/*`, uses `AntiforgeryEndpointFilter` on POST, PUT, PATCH and DELETE.
- **Policies:**
  - `FallbackPolicy` requires an authenticated user.
  - Public groups are `.AllowAnonymous()`.
  - `AdminOnly` requires the role `Catalog.Admin` (an Identity role).
- **Phase 4 resources** (contracts in requirements `docs/requirements/0002-admin-requirements.md`, approved by the user 2026-11-06):
  - `GET /admin/me`
  - CRUD for `brands`, `categories`, `products` (plus `publish` and `archive`), `products/{id}/variants` (including **`stockQuantity`**, admin-only; no delete), `products/{id}/images` (upload, §7.4), `products/{id}/notes`, `fragrance-notes`, `banners`
  - `concentrations`: **read-only** list (`0002` BR-A-19); no create, update or delete
  - Admin user management: create, list, deactivate, reactivate, unlock and set a temporary password for other admins
  - One role, `Catalog.Admin`, for all admin functions (D-A1)
- Admin uses numeric IDs. Updates carry `rowVersion` and return 409 on mismatch.
- **Audit:** admin writes set `UpdatedAtUtc` and `UpdatedBy` (the Identity user ID) and are logged as structured events. No persistent audit table (D-A3).

### 6.6 Validation rules

| Area | Rule |
|---|---|
| Query `page` / `pageSize` | int >= 1 / int 1–60 |
| Query `q` | trimmed; empty means absent; 2–100 characters; wildcards literal |
| Query `minPrice`/`maxPrice` | **integer** VND 0–10,000,000,000; decimals → 400; min <= max |
| Query `volume` | decimal > 0, <= 5000; max 20 values |
| Query `brand`/`category` | slug regex; max 20; invalid format → 400; unknown → no match |
| Query `concentration` | `^[A-Z_]{2,20}$`; max 20 |
| Query `sort` | one of the 5 values |
| Route `{slug}` | regex mismatch → 404 |
| Admin: names | required, trimmed, maximum length per the DB model |
| Admin: slug | regex, <= 200 characters, unique per entity → 409 |
| Admin: SKU | `^[A-Z0-9][A-Z0-9-_.]{0,63}$` after upper-casing, unique → 409 |
| Admin: variant | VolumeMl > 0 and <= 5000 (1 decimal place); Price integer >= 0; OriginalPrice null or > Price; StockQuantity integer >= 0; unique volume per product |
| Admin: image upload | multipart `file` <= 5 MB; signature JPEG, PNG or WebP that matches the declared type; `altText` <= 250; server-generated blob name; at most 6 images per product (D-A2), otherwise 422 `image_limit_reached` |
| Admin: banner `LinkUrl` | starts with `/` and not `//` |
| Admin: description | plain text, <= 10,000 characters |
| Admin: publish | active brand, >= 1 active variant and >= 1 image, otherwise 422 |
| Admin: password | Identity policy (§7.1) |

The database also enforces the critical rules (see `0002`).

## 7. Admin Authentication, Authorization and Image Upload

### 7.1 ASP.NET Core Identity with a cookie session (ADR-0010)

- **Why a cookie and not bearer tokens:**
  - The SPA and API share an origin.
  - An HttpOnly cookie keeps the credential out of JavaScript, so an XSS bug cannot steal it.
  - There is no token storage or refresh logic in the SPA.
  - `MapIdentityApi` is **not** used. It exposes `/register` and does not apply antiforgery. Small custom endpoints are used instead.
- **Setup:**
  - `AddIdentityCore<AdminUser>().AddRoles<IdentityRole<int>>().AddEntityFrameworkStores<AppDbContext>().AddSignInManager().AddDefaultTokenProviders()`
  - The cookie scheme `IdentityConstants.ApplicationScheme`.
- **Cookie:**
  - Name `__Host-utp-admin`.
  - `HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/`.
  - Sliding expiry of 30 minutes idle, with an absolute limit of 8 hours. No persistent "remember me".
  - The `OnRedirectToLogin` and `OnRedirectToAccessDenied` events return 401 or 403 ProblemDetails instead of redirecting.
  - `SecurityStampValidatorOptions.ValidationInterval` is 5 minutes, so deactivating an account or changing a password takes effect quickly.
- **Password policy:**
  - `RequiredLength = 12`, `RequireDigit`, `RequireUppercase`, `RequireLowercase`, `RequiredUniqueChars = 4`, and `RequireNonAlphanumeric = false`, because length matters more than symbol rules.
  - Default Identity PBKDF2 hashing.
- **Lockout:**
  - `MaxFailedAccessAttempts = 5`, `DefaultLockoutTimeSpan = 15 min`, `AllowedForNewUsers = true`.
  - `PasswordSignInAsync(..., lockoutOnFailure: true)`.
  - Unknown email, wrong password and locked account all return the same 401 (AC-A7).
- **Account policy:**
  - No self-registration. `RequireConfirmedAccount = false`; admins are created by admins.
  - **Bootstrap:** the Api runs a one-off command, `dotnet UTperfume.Api.dll bootstrap-admin`. It creates the first `Catalog.Admin` user from `Bootstrap:AdminEmail` and `Bootstrap:AdminPassword` (user-secrets, or a temporary App Service setting), and only if no admin exists. Running it against production needs user approval.
- **Two-factor:** TOTP is supported by Identity token providers but not enforced. Q-14, decided 2026-11-06: not required before go-live (risk accepted, `0002` R-A-07).

### 7.2 CSRF protection

- ASP.NET Core antiforgery is configured with `HeaderName = "X-XSRF-TOKEN"` and the cookie `__Host-utp-xsrf`. That cookie is readable by JavaScript and holds the request token.
- **Getting a token:**
  - `GET /api/v1/admin/auth/antiforgery` (anonymous) issues the token.
  - The token is re-issued after sign-in, because antiforgery tokens are bound to the user identity.
- `AntiforgeryEndpointFilter` calls `IAntiforgery.ValidateRequestAsync` on unsafe methods for every admin route, including sign-in, which prevents login CSRF. Failure returns 400 `antiforgery_invalid`.
  - A filter is needed because Minimal APIs only auto-validate form-bound endpoints, and the admin endpoints use JSON.
- `SameSite=Strict` on the session cookie is a second, independent layer.
- The openapi-fetch middleware reads the XSRF cookie and sets the header on POST, PUT, PATCH and DELETE.

### 7.3 Password reset and email (in Phase 4 scope)

- **Self-service reset is in scope for Phase 4 (P4-05):**
  - `POST /api/v1/admin/auth/password-reset/request { email }` always returns 202.
  - `POST /api/v1/admin/auth/password-reset/confirm { email, token, newPassword }` returns 204, or 400 for an invalid or expired token.
  - A token is valid for 1 hour (`DataProtectionTokenProviderOptions.TokenLifespan`) and is single-use, because the security stamp changes on reset.
  - A signed-in admin can change their own password with `POST /api/v1/admin/auth/change-password`.
- **Email sender:**
  - The interface is `IEmailSender<AdminUser>`, from Identity.
  - Development uses `LoggingEmailSender`, which never sends and logs only that a message was suppressed. It never logs the reset link or the reset token.
    - **Decided (OP-1, user 2026-11-07, option B):** reset tokens and reset links are never written to logs, in any environment; `0002` BR-A-31 holds without exception. Automated tests, including the password-reset flow integration tests (P4-25), obtain the reset link from a **test-only capturing email sender**, never from the log. The capturing sender is registered only in the integration test host (decision 2026-11-07), never in Development or any other environment. P4-05 delivers the capturing sender and owns two tests: the OP-1 log test, that the logs of a full request-and-confirm flow and of the change-password flow contain no reset token, reset link, password or password hash (decision 2026-11-07, applying BR-A-31); and the absence test, that a non-test host does not register the capturing sender (moved from P4-25 to P4-05 on 2026-11-07). P4-25 reuses the sender and both tests. No backend channel exposes reset links to Playwright or any other external client; the password-reset flow is covered by integration tests only (user decision 2026-11-07, P4-25 option A).
  - The production provider is **Gmail** over SMTP, and the sender address is decided (Q-12, 2026-11-06; see `0001` Q-12 and ADR-0010 §8; the address itself is the sender address the user named and is not recorded in the project documents). The production sender is task P4-05a. The Gmail app password (or equivalent credential) is a secret held only in the deployment secret store, never in the repository, configuration files or documents. Whether Gmail accepts the decided sender address for the provider account must be verified before production use.
  - Until the production sender is wired and its credential configured (and as a fallback afterwards), production reset is admin-assisted: another admin sets a temporary password. The flow, endpoints and tests ship in Phase 4 regardless (P4-05).
    - **Decided (OP-2, user 2026-11-07, option A):** outside Development, before P4-05a exists, a reset request returns 202 as usual, sends no email, and logs only the fact that no email was sent. That entry contains no token, no email address and no link. P4-05 provides the non-sending registration for environments other than Development; P4-05a replaces it with the Gmail sender. P4-05a remains blocked on the sender setup (Q-12), unchanged.
- **Data Protection keys** (used for cookies, antiforgery and reset tokens) must persist across App Service restarts and scale-out. Store them in Blob Storage (`PersistKeysToAzureBlobStorage`, from `Azure.Extensions.AspNetCore.DataProtection.Blobs`), protected with Key Vault if one is provisioned. This is decided before P5 deployment.

### 7.4 Image upload: through the API (ADR-0014)

- `POST /api/v1/admin/products/{id}/images` takes `multipart/form-data` with fields `file`, `altText` and `isPrimary`.
- **Request size:** limited to 5 MB per request with `RequestSizeLimit`. Kestrel and App Service limits are configured to match.
- **Processing steps:**
  1. Check the request size and type.
  2. Read the magic bytes (JPEG `FF D8 FF`, PNG `89 50 4E 47`, WebP `RIFF....WEBP`) and require them to match the declared type.
  3. Read width and height from the header if it is cheap; otherwise store them as null.
  4. Generate the blob path `products/{productId}/{guid}.{ext}`.
  5. Upload with `Content-Type` and `Cache-Control: public, max-age=31536000, immutable`.
  6. Insert the `ProductImage` row.
  7. If the database insert fails, delete the blob (best effort, and logged).
- **Response:** 201 with the admin image DTO.
- **Delete:** delete the row, then delete the blob. A failed blob delete is logged, and orphan cleanup is deferred.
- Brand logos, category images and banner images use the same service, under the prefixes `brands/`, `categories/` and `banners/`.
- **Credentials:** the App Service uses its Managed Identity with the *Storage Blob Data Contributor* role on the container. There are no account keys or SAS in configuration.
- **Public read (Q-13, approved 2026-11-06):** the `catalog-images` container allows anonymous blob-level read, with listing disabled. Only catalogue images are public: product images, brand logos, category images and banner images (D-A4); `dataprotection-keys` and any other container are private (ADR-0014 §5). The public URL is `Storage:Images:PublicBaseUrl` + `/` + `BlobPath`, so a CDN can be added later by changing configuration only.
- **Non-production storage account (D-12):** provisioned by the user. The Developer does not create Azure resources and uses Azurite until the user supplies the non-secret settings.

## 8. Database Model Summary

The full detail is in `docs/architecture/0002-database-model.md`.

| Table | Purpose |
|---|---|
| `catalog.Brand`, `catalog.Category` | Lookups with slugs and image blob paths |
| `catalog.Concentration` | Seeded lookup |
| `catalog.Product` | Fragrance in one concentration |
| `catalog.ProductVariant` | SKU, volume, **price as `bigint` VND**, stock (admin-only) |
| `catalog.ProductImage` | Blob path, metadata, order, one primary |
| `catalog.FragranceNote`, `catalog.ProductNote`, `catalog.ProductCategory` | Notes and links |
| `catalog.HomeBanner` | Hero banners |
| `identity.*` | ASP.NET Core Identity tables (Users, Roles, UserRoles, UserClaims, UserLogins, UserTokens, RoleClaims) |

Other key points:

- Slug uniqueness per table and SKU uniqueness.
- `(ProductId, VolumeMl)` is unique.
- `rowversion` on admin-editable tables.
- Availability is derived using `Catalog:LowStockThreshold`.

## 9. Security Considerations

- **Authentication and authorization:**
  - Identity cookie (§7.1) with fallback policy = authenticated, public groups explicitly anonymous, and `AdminOnly` on admin routes.
  - A test enumerates every `/api/v1/admin` endpoint and asserts it has `AdminOnly`, except the allowlisted `auth/*` endpoints.
  - Another test asserts that every admin unsafe-method endpoint has the antiforgery filter.
- **CSRF:** antiforgery header token plus `SameSite=Strict` (§7.2).
- **Credential attacks:** lockout, a generic failure response, a rate limit on sign-in and reset requests, and a 12-character minimum password.
- **Data exposure:** public DTOs are explicit projections. Stock quantity, status, flags, row versions and blob paths are never in public responses. AC-P2b is tested.
- **Injection:** parameterised EF Core LINQ only, and LIKE wildcards are escaped.
- **Uploads:**
  - Size limit and signature check.
  - Server-generated names; the client file name is never used as the path.
  - No SVG, which prevents script in images.
  - Correct `Content-Type` on each blob.
  - The images are served from a different origin (`*.blob.core.windows.net`) than the app.
- **XSS and CSP:** `default-src 'self'; img-src 'self' https://<account>.blob.core.windows.net data:; script-src 'self'; style-src 'self' 'unsafe-inline'; frame-ancestors 'none'`. The image host comes from configuration.
- **Transport:** HTTPS with HSTS, `X-Content-Type-Options: nosniff`, and `Referrer-Policy: strict-origin-when-cross-origin`.
- **Secrets:**
  - Nothing is committed.
  - Locally: `dotnet user-secrets`. The Azurite development connection string is a well-known non-secret.
  - Azure: App Service settings with Key Vault references, and Managed Identity to Azure SQL and Blob Storage.
  - GitHub OIDC for deployment.
  - The CI SQL password is a GitHub Actions secret used only by the throwaway CI container.
- **Logging:** passwords, cookies, reset tokens and PII are never logged. Admin sign-ins (success and failure) and writes are logged with the user ID and IP.
- **OWASP:**
  - A01: policy, tests, antiforgery
  - A02: HTTPS, Identity hashing
  - A03: parameterised queries
  - A04: upload validation
  - A05: headers, no detailed errors
  - A07: lockout, password policy, generic errors
  - A09: structured audit logs

## 10. Testing Strategy (mapped to CLAUDE.md quality gates)

**Docker is not available on the user's machine.** No test may depend on Docker locally. Testcontainers is not used (ADR-0013).

| Layer | Tooling | Key coverage | Gate |
|---|---|---|---|
| Unit | xUnit v3, FluentAssertions 7.x | Slug, availability thresholds (0, 1, T, T+1), integer discount, from-price, VND formatting helpers, image signature detection, Result→HTTP mapping | `dotnet test` |
| Integration | WebApplicationFactory + **real SQL Server** (see below); fake `IImageStorage` (in-memory); test-only capturing email sender, registered only in the integration test host and never in Development or any other environment (captured logs of the reset and change-password flows must contain no reset token, reset link, password or password hash, `0002` BR-A-31; P4-05 owns that log test and the test that a non-test host does not register the capturing sender) | All requirement ACs for E1–E11; no `stockQuantity` in public JSON; 401/403; antiforgery 400; lockout; reset token flow; upload validation (413/415); 429; migration applies to an empty DB | `dotnet test` |
| Contract | Regenerate `openapi/v1.json` and `schema.d.ts`, then `git diff --exit-code` | Contract sync; public schemas contain no `stockQuantity` | CI step |
| Frontend | Vitest + RTL + jsdom; `renderWithProviders` with a fresh QueryClient (retry off); `fetch` mocked at the client boundary | URL↔state hook, FilterPanel, Pagination, `keepPreviousData` behaviour, Price/VND formatting, AvailabilityBadge (three states), ApiError mapping, XSRF middleware | `npm run lint`, `npm run build`, `npm run test` |
| E2E | Playwright (Chromium) against API + built SPA + seeded database | Home, catalogue filter → URL → back, pagination, product variant switch, not-found | `npx playwright test` |
| Database | `dotnet ef migrations add` and an idempotent script reviewed | Upgrade and rollback notes | CI artifact + review |

### 10.1 Docker-free SQL Server for integration and E2E tests

- **Connection:** the test connection string comes from `TestDatabase:ConnectionString`, set by environment variable or the test project's user-secrets. The default is `Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=60`.
- **Local options:**
  1. **SQL Server Express LocalDB**, version 2019 or later (2022 preferred). It is installed with Visual Studio's *Data storage and processing* workload or the standalone SQL Server Express installer (LocalDB feature).
  2. **SQL Server 2022 Developer Edition** installed on Windows, using Windows authentication.
- **Isolation:**
  - Each test run creates a uniquely named database (`UTperfume_Test_{runId}`), applies migrations with `MigrateAsync`, and drops the database at the end.
  - Database tests run in one xUnit collection, sequentially.
  - Each test class resets data with a small helper that deletes rows in foreign-key order. Respawn is not used, so no new package.
- **CI (GitHub Actions, `ubuntu-latest`):**
  - A `services: mssql` container runs `mcr.microsoft.com/mssql/server:2022-latest` with `ACCEPT_EULA=Y` and `MSSQL_SA_PASSWORD: ${{ secrets.CI_SQL_SA_PASSWORD }}`, and has a health check.
  - The workflow builds `TestDatabase__ConnectionString` from the secret.
  - The Docker requirement is on GitHub's runner only, never on the user's machine.
- **Parity:** avoid SQL Server 2022-only T-SQL, because LocalDB may be 2019. CI runs SQL Server 2022 on Linux. Both default to `SQL_Latin1_General_CP1_CI_AS`.

### 10.2 Windows setup steps the Developer must verify (user has no Docker)

These steps are recorded in `docs/dev-setup.md` (P0-05).

1. `sqllocaldb versions` shows 15.x (2019) or later, and `sqllocaldb info MSSQLLocalDB` succeeds. If it is missing, install SQL Server Express LocalDB.
2. `sqllocaldb start MSSQLLocalDB` works. The first connection may take more than 15 s, hence `Connect Timeout=60`.
3. The current Windows user can create and drop databases on LocalDB. LocalDB is per-user, so a CI or service account will not see the developer's instance.
4. `dotnet ef database update` against LocalDB succeeds. This covers schemas, filtered unique indexes, `rowversion` and CHECK constraints.
5. Azurite runs without Docker through Node (`npx azurite --location .azurite --silent`), on ports 10000–10002, and is not blocked by a firewall or by existing Hyper-V port reservations.
6. `dotnet dev-certs https --trust` works, so the Vite proxy can reach the API over HTTPS.
7. `npx playwright install chromium` succeeds (on a corporate network a proxy may be needed).

## 11. Configuration, Logging, Hosting, CI/CD

**Configuration keys:**

| Key | Notes |
|---|---|
| `ConnectionStrings:Catalog` | |
| `Store:Currency` | Fixed `VND` |
| `Store:PublicBaseUrl` | The public origin, for example `https://shop.example.com`, with no path. Only the scheme and host are used (sitemap URLs), so the app cannot be served under a sub-path (ADR-0012: one same-origin App Service). Required and validated at startup outside Development. |
| `Catalog:NewBadgeDays` | Default 30 |
| `Catalog:LowStockThreshold` | Default 5, integer >= 1, validated at startup with `ValidateOnStart` |
| `RateLimiting:PublicPerMinute` | Default 120 per client IP per minute; 429 ProblemDetails with `Retry-After` |
| `RateLimiting:AuthPerMinute` | Default 5 (used from Phase 4) |
| `Storage:Images:ServiceUri` | |
| `Storage:Images:Container` | `catalog-images` |
| `Storage:Images:PublicBaseUrl` | |
| `Storage:Images:ConnectionString` | Local Azurite only; `UseDevelopmentStorage=true` |
| `Identity:*` | Cookie lifetimes |
| `Email:*` | Gmail SMTP (Q-12): sender address and account name are deployment configuration; the app password is a **secret**, set only in the deployment secret store or user-secrets, never committed |
| `Bootstrap:AdminEmail`, `Bootstrap:AdminPassword` | Secret; never committed |

- **Migrations:** never run at startup outside Development. A CI job applies them in non-production. Production migrations need explicit user approval.
- **Development seed:**
  - A Development-only seeder loads sample catalogue data and uploads bundled placeholder images to Azurite when it is reachable. Otherwise the UI shows its placeholder image.
  - Concentration is seeded with `HasData`.
  - The `Catalog.Admin` role is seeded with `HasData`. Admin users are created by `bootstrap-admin` only.
- **CI (`ci.yml`):**
  - backend job with the SQL Server service container
  - frontend job
  - contract drift check
  - E2E job with the SQL Server service container and Azurite started with `npx`
- **Azure resources (Phase 5, approval required):**
  - App Service with a system-assigned Managed Identity
  - Azure SQL
  - **Storage account** (StorageV2, LRS, HTTPS only, TLS 1.2 or later), with container `catalog-images` (anonymous blob-level read, Q-13) and container `dataprotection-keys` (private). The non-production account is provisioned by the user (D-12).
  - Key Vault (optional)
- **Observability:** App Service logs. Application Insights is optional and should be decided before the first deployment.

## 12. Future Compatibility (not implemented)

| Future feature | How the current design avoids blocking it |
|---|---|
| Cart, orders | Line items reference `ProductVariant.Id` and snapshot SKU, name, volume and price (bigint VND). |
| Stock reservation / ledger | `StockQuantity` stays as the balance. A `StockMovement` table and atomic decrement come later (ADR-0005). |
| Customer accounts | Separate from admin. Customers must not share the `Catalog.Admin` role or the admin cookie; they get a separate cookie name and a later ADR. |
| Promotions | Computed price at checkout. |
| Multi-currency | Out of scope. It would need a migration from `bigint` to `decimal` plus a currency column. |
| Reviews, wishlist, computed best sellers, filter by note | Unchanged from the original design. |
