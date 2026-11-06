# 0001 – Perfume Catalogue Requirements (Public Storefront + Admin Boundary)

- Status: **Accepted** (user approval 2026-11-05). Q-12, Q-13 and Q-14 decided 2026-11-06. Q-07 to Q-11 remain open with defaults (§8).
- Date: 2026-11-05 (revised after the user's decisions; revised 2026-11-06 for the Phase 4 decisions)
- Author: Architect
- Related: `docs/architecture/0001-solution-architecture.md`, `docs/architecture/0002-database-model.md`, `docs/decisions/0001`–`0014`, `docs/tasks/0001-phase-plan.md`, `docs/requirements/0002-admin-requirements.md` (draft)

## 1. Requirement Summary

NCTService.UTperfume is a perfume e-commerce website. This release delivers the **public, anonymous catalogue**:

- Home page.
- Catalogue listing with server-side pagination, search, filters and sort.
- Product detail.
- Browse by brand and by category.
- SEO-friendly URLs.

It also delivers an **admin boundary**, meaning an authenticated and authorized API surface, so catalogue data can be managed later. Admin UI and admin feature detail will be specified in a later requirements document (`0002`).

Cart, customer accounts, checkout, orders, payment, shipping, promotions, wishlist and reviews are **future** work. The design must not block them, but nothing is built for them now.

## 2. Scope

### In scope

- Public read-only catalogue API and React storefront:
  - Home
  - Catalogue
  - Product detail
  - Related products
  - Brand list and brand page
  - Category page
  - Filter options
- Domain and database model for:
  - Brand
  - Category
  - Concentration
  - Product
  - ProductVariant
  - ProductImage
  - FragranceNote / ProductNote
  - HomeBanner
- Admin API boundary: route prefix, authentication, authorization policy, and 401/403 behaviour tests.
- SEO basics: slug URLs, per-page title/meta, canonical URL, `sitemap.xml`, `robots.txt`.
- The Phase 0 foundation: repository, solution, CI, OpenAPI contract pipeline.

### Out of scope (this document)

- Admin UI and detailed admin CRUD rules. These are deferred to requirements `0002`. Only the boundary is in scope.
- Cart, checkout, orders, payment, shipping, customer accounts, promotions, wishlist, reviews.
- Stock movement ledger and stock reservation.
- Image upload in this release. Images are stored in Azure Blob Storage (ADR-0014), and the admin-only upload is delivered in Phase 4.
- Multi-currency and multi-language. The store currency is VND.
- Server-side rendering or prerendering. This would require a stack change.
- Production deployment and Azure provisioning. Both need explicit user approval.

## 3. Actors

| Actor | Description | Authentication |
|---|---|---|
| Visitor | Anonymous shopper browsing the catalogue | None |
| Admin | Staff member who manages catalogue data | Required; ASP.NET Core Identity cookie session, role `Catalog.Admin` (ADR-0010) |
| Search engine crawler | Indexes public pages | None |

## 4. User Stories

| ID | Story |
|---|---|
| US-01 | As a visitor, I want a home page with banners, featured perfumes, new arrivals, best sellers, popular brands and categories, so I can discover products quickly. |
| US-02 | As a visitor, I want to browse a paginated catalogue, so I can see all perfumes without loading everything at once. |
| US-03 | As a visitor, I want to search by perfume name, brand name or SKU, so I can find a specific product. |
| US-04 | As a visitor, I want to filter by brand, category, price range, concentration and volume, so I see only relevant products. |
| US-05 | As a visitor, I want to sort by price, newest or name. |
| US-06 | As a visitor, I want a product detail page with images, description, concentration, fragrance notes (top/heart/base), all variants with price, compare-at price and availability, and related products. |
| US-07 | As a visitor, I want to browse all brands and open a brand page that lists that brand's perfumes. |
| US-08 | As a visitor, I want to open a category page that lists perfumes in that category. |
| US-09 | As a visitor, I want readable, shareable URLs (e.g. `/perfumes/dior-sauvage-edp`), and filter state kept in the URL, so links and the back button work. |
| US-10 | As the business, I want public pages to be indexable (titles, descriptions, sitemap). |
| US-11 | As an admin, I want catalogue management endpoints that only authenticated admins can call. |

## 5. Business Rules

### 5.1 Visibility

- **BR-01:** A product is **publicly visible** only when all of the following are true:
  - `Status = Published`
  - its Brand is active
  - it has at least one active variant

  Otherwise it is treated as not found (404) and is excluded from all listings, home sections, related products, filter options and the sitemap.
- **BR-02:** An inactive Brand or Category is not returned by brand or category endpoints (404 by slug). Products of an inactive brand are hidden (BR-01). Products that are only linked to an inactive category remain visible, but that category is not offered as a filter option.
- **BR-03:** Inactive variants are never returned publicly and are ignored in every price, volume and availability calculation.

### 5.2 Product and variant

- **BR-04:** A Product represents one fragrance in one concentration, for example "Dior Sauvage Eau de Parfum".
  - Variants differ by volume.
  - EDT and EDP of the same line are **separate products** (ADR-0001, ADR-0002).
- **BR-05:** Each variant has the following:
  - a SKU (globally unique, case-insensitive, stored uppercase, max 64 characters)
  - a volume in ml (> 0, max 5000, one decimal place)
  - a price in whole VND (integer >= 0, no decimals)
  - an optional original/compare-at price in whole VND
  - a stock quantity (integer >= 0). It is **admin-only** and never returned by public endpoints.
  - an active flag
- **BR-06:** Within one product, variant volumes are unique (one 100 ml variant per product). Testers and gift sets are an open question (Q-07).
- **BR-07:** `OriginalPrice`, when present, must be **strictly greater** than `Price`. The storefront shows it as a struck-through compare-at price and may show a discount percentage, `floor((Original - Price) * 100 / Original)` (integer arithmetic, so it never overstates the discount).
- **BR-08: Availability** is derived from stock and is not stored. `T` is the low-stock threshold, set by the app setting `Catalog:LowStockThreshold` (integer >= 1, default 5; ADR-0005).

  | Status | Condition (active variant) |
  |---|---|
  | `InStock` | `StockQuantity > T` |
  | `LowStock` | `1 <= StockQuantity <= T` |
  | `OutOfStock` | `StockQuantity = 0` |

  A product is "in stock" (`inStock = true`) if any of its active variants is `InStock` or `LowStock`. **Public responses carry only the status, never the quantity.**
- **BR-09:** Every product has exactly one Concentration (lookup, ADR-0002) and exactly one Brand. It can have zero or more Categories.
- **BR-10: Currency is VND.**
  - All prices are whole Vietnamese dong with no minor units. The API returns them as JSON integers, and `currency` is always `"VND"`.
  - Price query parameters (`minPrice`, `maxPrice`) are whole-dong integers. Decimal values return 400.
  - The UI formats prices with `Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND', maximumFractionDigits: 0 })`, for example `1.250.000 ₫`.
  - `Store:Currency` is fixed to `VND` and is not user-configurable in this release.

### 5.3 Price, filter and sort semantics (ADR-0004)

- **BR-11: From-price.**
  - A product's *from-price* is `MIN(Price)` over its active variants.
  - If a volume filter is applied, the from-price is calculated only over active variants whose volume is in the selected volumes.
  - The listing shows "From {fromPrice}", plus the compare-at price of the same variant if that variant has one. If several variants share the minimum price, the one with the lowest volume is used.
- **BR-12:** The price filter (`minPrice`, `maxPrice`, both inclusive) is applied to the from-price.
- **BR-13:** The volume filter matches products that have at least one active variant with a volume in the selected set.
- **BR-14: Combining filters.**
  - Multi-value filters (brand, category, concentration, volume) use OR within one filter and AND across filters.
  - Unknown slugs or codes match nothing. They do not cause an error.
- **BR-15: Sort options.**

  | Value | Order |
  |---|---|
  | `newest` (default) | `PublishedAtUtc` descending |
  | `price_asc` | from-price ascending |
  | `price_desc` | from-price descending |
  | `name_asc` | name ascending |
  | `name_desc` | name descending |

  Every sort has a final tie-breaker of `Id` ascending, so pagination is deterministic.
- **BR-16: Search** (`q`) is trimmed and must be 2–100 characters. It matches a product when any of the following is true:
  - the product name contains `q`
  - the brand name contains `q`
  - a variant SKU equals `q`

  Matching is case-insensitive. `%`, `_` and `[` are treated literally. Accent-insensitive matching is an open question (Q-09).

### 5.4 Pagination (ADR-0008)

- **BR-17:**
  - `page` defaults to 1 and must be >= 1.
  - `pageSize` defaults to 24 and must be between 1 and 60.
  - A page beyond the last page returns 200 with an empty `items` array and correct totals.

### 5.5 Home merchandising

- **BR-18: Featured perfumes** are visible products with `IsFeatured = true`, ordered by `PublishedAtUtc` descending, maximum 8.
- **BR-19: New arrivals** are visible products ordered by `PublishedAtUtc` descending, maximum 8. A product shows a "New" badge when `PublishedAtUtc` is within the last 30 days. The number of days is configurable (`Catalog:NewBadgeDays`).
- **BR-20: Best sellers** are visible products with `IsBestSeller = true` (curated by an admin), maximum 8. Because no orders exist yet, this is **manual curation**. When orders exist it may be replaced by computed best sellers (future work, ADR-0005).
- **BR-21: Popular brands** are active brands with `IsFeatured = true` and at least one visible product, ordered by `DisplayOrder` then name, maximum 12.
- **BR-22: Home categories** are active categories with at least one visible product, ordered by `DisplayOrder` then name, maximum 12.
- **BR-23: Banners** are active `HomeBanner` rows where the current UTC time is inside the optional `[StartsAtUtc, EndsAtUtc)` window, ordered by `SortOrder`, maximum 5.
  - A banner link must be a site-relative path starting with `/`. This prevents open redirects and `javascript:` URLs.

### 5.6 Related products

- **BR-24: Related products** are visible products, excluding the current product, that share at least one category or the same brand. They are ranked as follows:
  1. number of shared categories, descending
  2. same brand first
  3. `PublishedAtUtc` descending
  4. `Id`

  The default limit is 8 and the maximum is 12.

### 5.7 Slugs and URLs (ADR-0006)

- **BR-25: Slug format.**
  - Slugs are lowercase ASCII, matching `^[a-z0-9]+(?:-[a-z0-9]+)*$`, 1–200 characters.
  - Slugs are unique per entity type (product, brand, category, note).
  - Slugs are generated from the name when not supplied. Diacritics are transliterated: `è` becomes `e`, and Vietnamese `đ` becomes `d`.
- **BR-26:** Slug changes after publication are allowed in the admin. Old-slug 301 redirects are deferred (Q-08). An old slug returns 404.

### 5.8 Admin boundary (ADR-0007, ADR-0010)

- **BR-27: Admin route and access.**
  - Every admin endpoint is under `/api/v1/admin/`.
  - It requires an authenticated user who satisfies the `AdminOnly` policy.
  - The server returns 401 when the caller is unauthenticated and 403 when the caller is authenticated but not an admin.
  - Hiding UI elements in the frontend is not authorization.
- **BR-28:** Public endpoints never return any of the following:
  - Draft or Archived products, or inactive variants
  - **stock quantities**
  - admin-only fields (status, flags, timestamps other than publish date, row versions, blob paths)
  - internal IDs that are not needed
- **BR-29: Admin accounts** (ASP.NET Core Identity, ADR-0010):
  - There is no self-registration. The first admin is created by a bootstrap command, and later admins are created by an existing admin.
  - Sign-in uses an HttpOnly session cookie.
  - Every state-changing admin request must carry a valid antiforgery token.
  - The account is locked out after 5 consecutive failed sign-ins, for 15 minutes.
  - Passwords must be at least 12 characters and include upper case, lower case and a digit.
  - Self-service password reset by email is in Phase 4 scope.
- **BR-30: Image upload** (ADR-0014):
  - Only admins can upload, and uploads go through the API.
  - Accepted formats are JPEG, PNG and WebP, verified by file signature as well as by declared content type. The maximum size is 5 MB.
  - Files are stored in Azure Blob Storage under a server-generated name. The original file name is never used as the path.
  - **Public read (Q-13, decided 2026-11-06):** blobs in the `catalog-images` container are anonymously readable by URL (blob-level access, listing disabled). Only catalogue images are public. Catalogue images are product images, brand logos, category images and banner images (D-A4 in `0002`, decided 2026-11-06). No other blob is public: the `dataprotection-keys` container and any future container stay private, and nothing other than catalogue display images may be written to `catalog-images`.

## 6. Acceptance Criteria (testable)

### Home – `GET /api/v1/home`

- **AC-H1:** Given 10 visible featured products, when I request home, then `featuredProducts` contains 8 items ordered by `publishedAtUtc` descending.
- **AC-H2:** Given a featured product whose brand is inactive, when I request home, then that product does not appear in any section.
- **AC-H3:** Given a banner with `EndsAtUtc` in the past, when I request home, then it is not in `banners`.
- **AC-H4:** Given no data, when I request home, then I receive 200 with every section as an empty array.
- **AC-H5:** Given a featured brand with no visible products, when I request home, then it is not in `popularBrands`.

### Catalogue – `GET /api/v1/products`

- **AC-C1:** Given 50 visible products, when I request `page=2&pageSize=24`, then I receive 24 items, `page=2`, `totalItems=50`, `totalPages=3`.
- **AC-C2:** Given 50 visible products, when I request `page=10`, then I receive 200 with `items=[]` and `totalItems=50`.
- **AC-C3:** When I request `page=0`, `pageSize=61`, `minPrice=-1`, `minPrice=100&maxPrice=50`, an unknown `sort`, or `q` of 1 or more than 100 characters, then I receive 400 ValidationProblemDetails. The `errors` object is keyed by the offending parameter name.
- **AC-C4:** Given product A with active variants priced 1,500,000 and 3,200,000 VND, when I filter `minPrice=2000000`, then A is not returned. Its from-price is 1,500,000.
- **AC-C5:** Given product A with active variants 30 ml @ 1,500,000 and 100 ml @ 3,200,000, when I filter `volume=100&minPrice=2000000`, then A is returned with `fromPrice=3200000`.
- **AC-C5b:** When I request `minPrice=1500000.5`, then I receive 400. Prices are whole dong.
- **AC-C6:** Given products from brands `dior` and `chanel`, when I filter `brand=dior&brand=chanel&concentration=EDP`, then only EDP products of either brand are returned.
- **AC-C7:** Given a filter `brand=unknown-brand`, then I receive 200 with an empty result.
- **AC-C8:** Given `sort=price_asc`, then items are ordered by from-price ascending, with ties broken by `id`. Two consecutive pages never contain the same product.
- **AC-C9:** Given product "Sauvage" by brand "Dior", when `q=dior` or `q=SAUV`, then it is returned. When `q=` equals one of its SKUs, it is returned.
- **AC-C10:** Given `q=100%`, then `%` is matched literally. It is not treated as a wildcard.
- **AC-C11:** Given a Draft product, a product with only inactive variants, and a product of an inactive brand, then none of them is returned and none is counted in `totalItems`.
- **AC-C12:** Given a variant with `OriginalPrice=4000000, Price=3200000` that sets the from-price, then the item shows `fromPrice=3200000`, `fromOriginalPrice=4000000` and `currency="VND"`. Both values are JSON integers.

### Product detail – `GET /api/v1/products/{slug}`

- **AC-P1:** Given a visible product, when I request its slug, then I receive 200 with:
  - brand, concentration and categories
  - images ordered by `sortOrder`, primary first
  - notes grouped as `top`, `heart` and `base`, each ordered by `sortOrder`
  - active variants ordered by volume, each with `sku`, `volumeMl`, `price`, `originalPrice`, `discountPercent` and `availability`
- **AC-P2:** Given `Catalog:LowStockThreshold=5`:
  - a variant with `StockQuantity=0` has availability `OutOfStock`
  - a variant with `StockQuantity` between 1 and 5 has `LowStock`
  - a variant with `StockQuantity=6` has `InStock`
- **AC-P2b:** Given any public endpoint response (E1–E9), then no `stockQuantity` field (or any other quantity field) is present. This is verified against the response JSON and against the OpenAPI document.
- **AC-P3:** Given an inactive variant, then it is not in `variants`.
- **AC-P4:** Given a Draft, Archived or unknown slug, or a product of an inactive brand, then I receive 404 ProblemDetails. The response does not reveal which of these it is.
- **AC-P5:** Given a slug in uppercase or with invalid characters, then I receive 404. Slug lookup is exact and slugs are always lowercase.

### Related – `GET /api/v1/products/{slug}/related`

- **AC-R1:** Given product A sharing 2 categories with B and 1 with C, then B is ranked before C, and A is never in its own result.
- **AC-R2:** When `limit=13`, then 400. When `limit` is omitted, then at most 8 items. Given an unknown slug, then 404.

### Brands and categories

- **AC-B1:** `GET /api/v1/brands` returns active brands ordered by name, each with `productCount` equal to its number of visible products. Brands with 0 visible products are included only if they are active. Whether to show empty brands is open question Q-10; the default is to include them.
- **AC-B2:** `GET /api/v1/brands/{slug}` returns 200 for an active brand and 404 for an inactive or unknown brand.
- **AC-B3:** The brand page lists products through `GET /api/v1/products?brand={slug}`, with the same rules as the catalogue.
- **AC-K1:** `GET /api/v1/categories` and `GET /api/v1/categories/{slug}` behave the same as the brand endpoints, ordered by `DisplayOrder` then name.

### Filter options – `GET /api/v1/catalog/filters`

- **AC-F1:** It returns the brands, categories and concentrations that have at least one visible product, the distinct active-variant volumes of visible products in ascending order, and `priceRange {min, max}` over the from-prices of visible products (`null` when there are none).

### SEO and frontend

- **AC-S1:** Given a product detail page, then the document title is `{Product name} – {Brand} | UTperfume` and a meta description is present. The meta description is `MetaDescription`, or a fallback built from the short description.
- **AC-S2:** Given `/perfumes?brand=dior&page=2`, when I reload or share the URL, then the same filters and page are shown. Browser back restores the previous filter state.
- **AC-S3:** `GET /sitemap.xml` lists the home page, `/perfumes`, every visible product, every active brand and every active category, using absolute URLs.
- **AC-S4:** Given an unknown client route, then the SPA shows a "not found" page. Given an unknown `/api/...` route, then the API returns 404 ProblemDetails, not `index.html`.

### Admin boundary

- **AC-A1:** Given no token or cookie, when I call any `/api/v1/admin/*` endpoint, then 401.
- **AC-A2:** Given an authenticated user without the admin role, then 403.
- **AC-A3:** Given an authenticated admin, then the call is authorized and reaches the endpoint (verified with a probe endpoint, e.g. `GET /api/v1/admin/me`).
- **AC-A4:** Given any public endpoint, when it is called without credentials, then it succeeds. Public endpoints are explicitly anonymous.
- **AC-A5:** Given an authenticated admin, when a POST, PUT, PATCH or DELETE to `/api/v1/admin/*` has no valid antiforgery token, then 400 ProblemDetails (`code=antiforgery_invalid`) and no change is made.
- **AC-A6:** Given 5 consecutive failed sign-ins for one account, when the 6th attempt uses the correct password within 15 minutes, then sign-in fails. The response is identical to a wrong-password response, so it does not reveal the lockout.
- **AC-A7:** Given an unknown email, a wrong password, or a locked account, then sign-in returns the same 401 ProblemDetails.
- **AC-A8:** The session cookie is set with `HttpOnly`, `Secure` and `SameSite=Strict`. Sign-out invalidates it, and the security stamp is refreshed after a password change.
- **AC-A9:** A password reset request always returns 202, whether or not the email exists. A reset token is single-use and expires after 1 hour.
- **AC-A10:** An image upload by an admin that is over 5 MB is rejected with 413 ProblemDetails. An upload that is not JPEG/PNG/WebP by signature, or that claims a different content type than its bytes, is rejected with 415 ProblemDetails with `code=unsupported_image_type`. An empty or missing file is rejected with 400. In every case nothing is stored. (Aligned with `0002` BR-A-23, BR-A-24, AC-A-31 and AC-A-32.)

### Cross-cutting

- **AC-X1:** Every non-2xx response from `/api/*` is RFC 9457 ProblemDetails with a `traceId`. No stack trace is returned outside Development.
- **AC-X2:** Public GET responses include `Cache-Control: public, max-age=60`.
- **AC-X3:** Rate limiting on `/api/v1/products` and the other public endpoints returns 429 ProblemDetails when it is exceeded. The default limit is 120 requests per minute per IP and is configurable.

## 7. Assumptions (items marked "confirmed" were decided by the user)

- A-01: The store has a single currency (**VND, confirmed**) and a single language for the UI. Product names and descriptions may contain non-ASCII characters (Vietnamese or French), so they are stored as `nvarchar`.
- A-02: EDT and EDP of the same line are separate products. Variants differ only by volume.
- A-03: Categories are a flat list (no hierarchy), and a product can belong to several categories (e.g. "Men", "Niche").
- A-04: Gender is modelled as a category (e.g. Men/Women/Unisex), not as a separate attribute.
- A-05: "Best sellers" and "featured" are manual admin flags until orders exist.
- A-06: The catalogue is fewer than 10,000 products and 50,000 variants, so computing the from-price at query time is acceptable.
- A-07: Images are stored in Azure Blob Storage (**confirmed**). The database stores the blob path and metadata, and the public URL is built from configuration (ADR-0014). Anonymous read on the `catalog-images` container is **confirmed** (Q-13).
- A-08: Product descriptions are plain text with line breaks, not HTML. This avoids stored-XSS risk.
- A-09: Exact stock quantity is **not** shown publicly (**confirmed**). Only `InStock`, `LowStock` or `OutOfStock` is shown.
- A-10: The SPA is client-side rendered. SEO relies on crawlers that execute JavaScript plus the sitemap (see risk R-01).

## 8. Open Questions

### 8.1 Resolved by the user (2026-11-05)

| ID | Question | Decision |
|---|---|---|
| Q-01 | Admin authentication model | ASP.NET Core Identity, cookie session (ADR-0010) |
| Q-02 | FluentAssertions licence | Stay on 7.x (Apache-2.0) (ADR-0013) |
| Q-03 | Docker availability | Not available on the user's machine. Use LocalDB or a local SQL Server, plus a SQL Server service container in GitHub Actions. No Testcontainers (ADR-0013). |
| Q-04 | Store currency | VND, whole dong, no decimals (BR-10) |
| Q-05 | Image storage | Azure Blob Storage; admin-only upload (ADR-0014) |
| Q-06 | Public stock quantity | Not shown; status only (BR-08) |
| — | Git repository and GitHub remote | Approved |
| — | TanStack Query | Approved |

### 8.1a Resolved by the user (2026-11-06)

| ID | Question | Decision |
|---|---|---|
| Q-13 | Anonymous read on the image container | **Approved.** Anonymous blob-level read on `catalog-images` only (listing disabled); no other blob or container is public (BR-30, ADR-0014). The public-read scope is: **product images, brand logos, category images and banner images**, all stored in that one container (D-A4 in `0002`, decided 2026-11-06). |
| D-12 | Who provisions the non-production Azure storage account | **The user provisions it.** The Developer must not create Azure resources (`docs/tasks/0001-phase-plan.md`). |
| Q-12 | Email provider and sender identity for admin password reset | **Decided.** Provider: Gmail, using the provider account the user named. Sender address: the sender address the user named (not recorded in the project documents). Operational requirements: the Gmail app password (or equivalent credential) is a secret, stored only in the deployment secret store, never in the repository, configuration files or documents; the provider account address is not recorded in the project documents. Risk: Gmail sending limits apply, and a personal Gmail account is a non-enterprise production dependency; accepted by the user, to be reviewed before go-live (`0002` R-A-06). **Open point for the implementer:** Gmail normally sends only as the authenticated account or a verified "Send mail as" alias; whether the sender address is usable with the provider account must be verified before production use (ADR-0010 §8). |
| Q-14 | Require two-factor sign-in (TOTP) for admins? | **Not required before go-live.** Supported by Identity but not enforced. Security risk accepted by the user (`0002` R-A-07). |

### 8.2 Still open

| ID | Question | Blocking? | Default if not answered |
|---|---|---|---|
| Q-07 | Testers, gift sets and minis with the same volume as an existing variant? | No | Unique (ProductId, VolumeMl); revisit |
| Q-08 | 301 redirects for changed slugs, and behaviour for archived products (404 vs 410)? | No | 404; redirects deferred |
| Q-09 | Accent-insensitive search (e.g. "hermes" matches "Hermès", "nuoc hoa" matches "nước hoa")? This matters more now that the store is Vietnamese. | No | Case-insensitive only (database default `_CI_AS`) |
| Q-10 | Show active brands or categories that have no visible products? | No | Lists include them; home sections exclude them |
| Q-11 | Should a product with all variants out of stock be listed, and can it be sorted last? | No | Listed, normal sort, badge "Out of stock" |