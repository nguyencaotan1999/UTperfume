# 0002 – Database Model (SQL Server / Azure SQL)

- Status: **Accepted** (user approval 2026-11-05)
- Date: 2026-11-05 (revised: VND money, blob-path images, low-stock threshold, Identity schema)
- Related: `docs/architecture/0001-solution-architecture.md` §8; ADR-0001 to ADR-0006, ADR-0010, ADR-0014
- No migration exists yet. The initial migration is task P1-02 in `docs/tasks/0001-phase-plan.md`.

## 1. Conventions

| Topic | Convention |
|---|---|
| Schema | `catalog` for every table in §3. ASP.NET Core Identity tables live in `identity` (§3.13). Future modules use their own schema (`sales`, `customer`). All tables are in one DbContext (`AppDbContext`) with one migration history. |
| Primary keys | `int IDENTITY` named `Id`. `Concentration` uses `smallint`. Public URLs use slugs, not IDs. |
| Timestamps | `datetime2(3)` in UTC, columns suffixed `Utc`. Set by the application through `TimeProvider`, not by a database default, so tests stay deterministic. |
| Audit fields | Admin-editable tables have `CreatedAtUtc`, `UpdatedAtUtc` and `UpdatedBy nvarchar(100) NULL`. A full audit-history table is deferred. |
| Concurrency | `RowVersion rowversion` on admin-editable tables, mapped with `IsRowVersion()`. |
| Money | **VND, whole dong:** `bigint` (C# `long`) with CHECK constraints, no fractional part. `bigint` is used rather than `decimal(18,0)` so that the domain, the DTOs and OpenAPI (`integer`/`int64`) all use one integer type, with no value converter. |
| Blob references | Images are stored as `BlobPath varchar(300)`, relative to the `catalog-images` container, e.g. `products/42/3f2a….webp`. Full URLs are never stored; the API builds them from `Storage:Images:PublicBaseUrl` (ADR-0014). |
| Text | Display text is `nvarchar` (handles Vietnamese and accented names). Slugs, SKUs and codes are `varchar`. Explicit lengths throughout; no `nvarchar(max)` except `Product.Description`. |
| Enums | Fixed, code-defined sets are stored as `tinyint` with a CHECK constraint (`ProductStatus`, `NoteType`). |
| Collation | Database default (Azure SQL `SQL_Latin1_General_CP1_CI_AS`): case-insensitive, accent-sensitive (see Q-09). |
| Deletes | `ON DELETE NO ACTION` by default. `CASCADE` only for rows fully owned by a Product aggregate (images, note links, category links). |
| Naming | Primary keys `PK_<Table>`, foreign keys `FK_<Table>_<Ref>`, unique indexes `UX_<Table>_<Cols>`, non-unique indexes `IX_<Table>_<Cols>`, check constraints `CK_<Table>_<Rule>`. |

## 2. Entity-Relationship Overview

```
Brand 1───N Product N───1 Concentration
              │ 1
   ┌──────────┼───────────┬──────────────┐
   N          N           N              N
ProductVariant ProductImage ProductNote  ProductCategory
                              N│            │N
                               1            1
                         FragranceNote    Category

HomeBanner (standalone)
```

## 3. Tables

### 3.1 `catalog.Brand`

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | int IDENTITY | NO | PK |
| Name | nvarchar(150) | NO | UX |
| Slug | varchar(200) | NO | UX; CK slug format (see 3.12) |
| Description | nvarchar(2000) | YES | plain text |
| LogoBlobPath | varchar(300) | YES | Blob path under `brands/` (ADR-0014) |
| IsFeatured | bit | NO | default 0 ("popular brands", BR-21) |
| DisplayOrder | int | NO | default 0 |
| IsActive | bit | NO | default 1 |
| CreatedAtUtc, UpdatedAtUtc | datetime2(3) | NO | |
| UpdatedBy | nvarchar(100) | YES | |
| RowVersion | rowversion | NO | |

Indexes:

- `UX_Brand_Slug`
- `UX_Brand_Name`

The table is small, so no further indexes are needed.

### 3.2 `catalog.Category`

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | int IDENTITY | NO | PK |
| Name | nvarchar(100) | NO | UX |
| Slug | varchar(200) | NO | UX; CK slug format |
| Description | nvarchar(2000) | YES | |
| ImageBlobPath | varchar(300) | YES | Blob path under `categories/` |
| DisplayOrder | int | NO | default 0 |
| IsActive | bit | NO | default 1 |
| CreatedAtUtc, UpdatedAtUtc, UpdatedBy, RowVersion | — | — | as Brand |

Indexes:

- `UX_Category_Slug`
- `UX_Category_Name`

Categories are a flat list (assumption A-03). If a hierarchy is needed later, add a nullable `ParentId` FK. That is additive and non-breaking.

### 3.3 `catalog.Concentration` (lookup, ADR-0002)

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | smallint | NO | PK (not identity; seeded IDs are stable) |
| Code | varchar(20) | NO | UX; CK `Code NOT LIKE '%[^A-Z_]%'` |
| Name | nvarchar(50) | NO | e.g. "Eau de Parfum" |
| SortOrder | smallint | NO | |
| IsActive | bit | NO | default 1 |

Seed data through `HasData`:

| Id | Code | Name |
|---|---|---|
| 1 | PARFUM | Parfum |
| 2 | EXTRAIT | Extrait de Parfum |
| 3 | EDP | Eau de Parfum |
| 4 | EDT | Eau de Toilette |
| 5 | EDC | Eau de Cologne |
| 6 | EAU_FRAICHE | Eau Fraîche |

The final list needs business confirmation.

### 3.4 `catalog.Product`

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | int IDENTITY | NO | PK |
| BrandId | int | NO | FK → Brand (NO ACTION) |
| ConcentrationId | smallint | NO | FK → Concentration (NO ACTION) |
| Name | nvarchar(200) | NO | e.g. "Sauvage" or "Sauvage Eau de Parfum" (display convention to agree) |
| Slug | varchar(200) | NO | UX; CK slug format |
| ShortDescription | nvarchar(500) | YES | |
| Description | nvarchar(max) | YES | plain text; the app enforces <= 10,000 characters |
| Status | tinyint | NO | 0 = Draft, 1 = Published, 2 = Archived; CK `Status IN (0,1,2)`; default 0 |
| PublishedAtUtc | datetime2(3) | YES | set on first publish; CK `Status <> 1 OR PublishedAtUtc IS NOT NULL` |
| IsFeatured | bit | NO | default 0 |
| IsBestSeller | bit | NO | default 0 (manual curation, BR-20) |
| MetaTitle | nvarchar(200) | YES | SEO override |
| MetaDescription | nvarchar(320) | YES | SEO override |
| CreatedAtUtc, UpdatedAtUtc, UpdatedBy, RowVersion | — | — | |

Indexes:

| Index | Columns | Purpose |
|---|---|---|
| `UX_Product_Slug` | `(Slug)` | Unique slug |
| `IX_Product_BrandId` | `(BrandId)` | FK and brand filter |
| `IX_Product_ConcentrationId` | `(ConcentrationId)` | FK and concentration filter |
| `IX_Product_Status_PublishedAtUtc` | `(Status, PublishedAtUtc DESC, Id)` INCLUDE `(BrandId, ConcentrationId, Name, Slug, IsFeatured, IsBestSeller)` | Default "newest" listing and home sections |
| `IX_Product_Status_Name` | `(Status, Name, Id)` | Name sort |

### 3.5 `catalog.ProductVariant`

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | int IDENTITY | NO | PK |
| ProductId | int | NO | FK → Product (**NO ACTION**; variants are deactivated, never deleted once referenced by future orders) |
| Sku | varchar(64) | NO | UX; stored uppercase |
| VolumeMl | decimal(6,1) | NO | CK `VolumeMl > 0 AND VolumeMl <= 5000` |
| Price | bigint | NO | Whole VND; CK `Price >= 0` |
| OriginalPrice | bigint | YES | Whole VND compare-at price; CK `OriginalPrice IS NULL OR OriginalPrice > Price` |
| StockQuantity | int | NO | CK `StockQuantity >= 0`; default 0. **Admin-only**: never projected into public DTOs |
| IsActive | bit | NO | default 1 |
| CreatedAtUtc, UpdatedAtUtc, UpdatedBy, RowVersion | — | — | |

Indexes:

| Index | Columns | Purpose |
|---|---|---|
| `UX_ProductVariant_Sku` | `(Sku)` | Unique SKU |
| `UX_ProductVariant_ProductId_VolumeMl` | `(ProductId, VolumeMl)` | Unique volume per product (BR-06); also serves as the FK index |
| `IX_ProductVariant_ProductId_IsActive` | `(ProductId, IsActive)` INCLUDE `(Price, OriginalPrice, VolumeMl, StockQuantity)` | From-price, availability and volume aggregation per product |
| `IX_ProductVariant_VolumeMl` | `(VolumeMl)` INCLUDE `(ProductId, IsActive, Price)` | Volume filter and distinct-volume list |

**Stock and availability (ADR-0005):**

- Availability is **not stored**. It is derived in the query projection from `StockQuantity` and the threshold `T`.
- `T` comes from the application setting `Catalog:LowStockThreshold` (default 5, integer >= 1, validated at startup). It is not a database column.
- The mapping is: `0` → `OutOfStock`, `1..T` → `LowStock`, `> T` → `InStock`.
- The threshold is passed into the EF query as a parameter, so the status is calculated in SQL as a `CASE` expression. The quantity never leaves the database for public queries.
- A per-variant override column (`LowStockThreshold int NULL`) is deferred. It would be additive.

### 3.6 `catalog.ProductImage`

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | int IDENTITY | NO | PK |
| ProductId | int | NO | FK → Product (CASCADE) |
| BlobPath | varchar(300) | NO | UX; `products/{productId}/{guid}.{ext}`; server-generated |
| ContentType | varchar(50) | NO | CK `ContentType IN ('image/jpeg','image/png','image/webp')` |
| SizeBytes | int | NO | CK `SizeBytes > 0 AND SizeBytes <= 5242880` |
| Width | int | YES | pixels, if detected |
| Height | int | YES | pixels, if detected |
| OriginalFileName | nvarchar(255) | YES | for admin display only; never used in a path or a header |
| AltText | nvarchar(250) | YES | falls back to the product name |
| SortOrder | int | NO | default 0 |
| IsPrimary | bit | NO | default 0 |
| CreatedAtUtc | datetime2(3) | NO | |
| CreatedBy | nvarchar(100) | YES | Identity user ID of the uploader |

Indexes:

- `UX_ProductImage_BlobPath (BlobPath)`
- `IX_ProductImage_ProductId_SortOrder (ProductId, SortOrder)`
- **Filtered unique** `UX_ProductImage_ProductId_Primary (ProductId) WHERE IsPrimary = 1`, which allows at most one primary image per product

Images are product-level. Per-variant images (e.g. a different bottle per size) are deferred. A nullable `VariantId` can be added later without breaking anything.

Deleting a Product row cascades to its image rows, but **not** to the blobs. The application deletes blobs explicitly, and orphan blob cleanup is deferred (ADR-0014). Products are archived rather than deleted in normal use.

### 3.7 `catalog.FragranceNote`

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | int IDENTITY | NO | PK |
| Name | nvarchar(100) | NO | UX (e.g. "Bergamot") |
| Slug | varchar(200) | NO | UX |
| CreatedAtUtc | datetime2(3) | NO | |

### 3.8 `catalog.ProductNote` (ADR-0003)

| Column | Type | Null | Notes |
|---|---|---|---|
| ProductId | int | NO | PK part; FK → Product (CASCADE) |
| NoteType | tinyint | NO | PK part; 1 = Top, 2 = Heart, 3 = Base; CK `NoteType IN (1,2,3)` |
| FragranceNoteId | int | NO | PK part; FK → FragranceNote (NO ACTION) |
| SortOrder | smallint | NO | default 0 |

- PK: `(ProductId, NoteType, FragranceNoteId)`. The same note may appear in different tiers, but only once per tier.
- Index: `IX_ProductNote_FragranceNoteId (FragranceNoteId)`, for the FK and a future filter by note.

### 3.9 `catalog.ProductCategory`

| Column | Type | Null | Notes |
|---|---|---|---|
| ProductId | int | NO | PK part; FK → Product (CASCADE) |
| CategoryId | int | NO | PK part; FK → Category (NO ACTION) |

- PK: `(ProductId, CategoryId)`
- Index: `IX_ProductCategory_CategoryId_ProductId`

### 3.10 `catalog.HomeBanner`

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | int IDENTITY | NO | PK |
| Title | nvarchar(150) | NO | |
| Subtitle | nvarchar(300) | YES | |
| ImageBlobPath | varchar(300) | NO | Blob path under `banners/` |
| AltText | nvarchar(250) | NO | |
| LinkUrl | nvarchar(500) | YES | CK `LinkUrl IS NULL OR (LinkUrl LIKE '/%' AND LinkUrl NOT LIKE '//%')` |
| SortOrder | int | NO | default 0 |
| IsActive | bit | NO | default 1 |
| StartsAtUtc | datetime2(3) | YES | |
| EndsAtUtc | datetime2(3) | YES | CK `StartsAtUtc IS NULL OR EndsAtUtc IS NULL OR EndsAtUtc > StartsAtUtc` |
| CreatedAtUtc, UpdatedAtUtc, UpdatedBy, RowVersion | — | — | |

Index: `IX_HomeBanner_IsActive_SortOrder`.

### 3.11 Slug uniqueness

- Uniqueness is enforced by unique indexes per table. The application pre-checks and returns 409. A `DbUpdateException` caused by a unique-index violation (SQL error 2601 or 2627) is also mapped to 409, to cover concurrent inserts.
- Slug matching is exact. Slugs are stored lowercase, and under the case-insensitive collation `Dior` and `dior` would collide anyway, which is the intended behaviour.

### 3.12 Slug CHECK constraint

```sql
CK_<Table>_SlugFormat:
Slug NOT LIKE '%[^a-z0-9-]%' COLLATE Latin1_General_BIN2
AND Slug NOT LIKE '-%' AND Slug NOT LIKE '%-' AND Slug NOT LIKE '%--%'
```

The application also validates the full regex.

### 3.13 `identity` schema (ASP.NET Core Identity, ADR-0010)

- These are the standard Identity tables, renamed into schema `identity` through `builder.HasDefaultSchema` overrides or `ToTable("…", "identity")`:
  - `Users`, `Roles`, `UserRoles`, `UserClaims`, `UserLogins`, `UserTokens`, `RoleClaims`
- Keys are `int`, using `IdentityUser<int>` and `IdentityRole<int>`.
- `AdminUser : IdentityUser<int>` adds these columns:
  - `DisplayName nvarchar(100) NOT NULL`
  - `IsActive bit NOT NULL default 1`. An inactive user cannot sign in, and the check is enforced in the sign-in endpoint.
  - `CreatedAtUtc datetime2(3) NOT NULL`
- Identity's own columns provide lockout (`LockoutEnd`, `AccessFailedCount`, `LockoutEnabled`), `SecurityStamp` and `ConcurrencyStamp`.
- **Invariant: user name equals email (review L-4, 2026-11-07).**
  - Every admin user is stored with `UserName` equal to `Email` (so `NormalizedUserName` equals `NormalizedEmail`). The `bootstrap-admin` command does this, and every other code path that creates a user or changes an email must do the same; **P4-09 (admin users API) must keep it**.
  - Email uniqueness is enforced by the **unique** index `UserNameIndex` on `NormalizedUserName` (filtered `IS NOT NULL`), through this invariant.
  - `EmailIndex` on `NormalizedEmail` is **non-unique** (the Identity default). It speeds up lookups but does not prevent duplicate emails on its own.
  - If the invariant breaks (a user name that differs from the email), the database no longer prevents two users with the same email; only the application checks remain (Identity `RequireUniqueEmail` and the `duplicate_email` pre-check, `0002` BR-A-07, AC-A-08), and a concurrent insert can pass both. Sign-in by email then fails for that address, because sign-in looks the user up with `FindByEmailAsync`, which expects at most one user per normalised email and throws when it finds two.
- The role `Catalog.Admin` is seeded with `HasData`, using a fixed Id and `ConcurrencyStamp`. Users are never seeded by migrations; they come from the `bootstrap-admin` command.
- `UpdatedBy` and `CreatedBy` columns in `catalog` hold the Identity user ID as text. There is deliberately no FK, so audit values survive if a user is removed.
- These tables are added by a **separate migration, `AddAdminIdentity`, in Phase 4 (P4-02)**. It is additive.

## 4. Query Notes (Performance)

- **From-price:** a correlated aggregate in the EF projection. In SQL this is `MIN(v.Price) WHERE v.ProductId = p.Id AND v.IsActive = 1 [AND v.VolumeMl IN (...)]`, and it is used for the filter, the sort and the display (ADR-0004). This is acceptable under assumption A-06.
- **Escalation path:** if a measured p95 above 300 ms appears, add a denormalised `Product.MinActivePrice bigint NULL` maintained by the application on variant writes. This is additive and non-breaking. It does not apply when a volume filter is active.
- **Visibility predicate:** `p.Status = 1 AND brand.IsActive = 1 AND EXISTS(active variant)`. It lives in one reusable `IQueryable` extension.
- **Count query:** `totalItems` uses the same predicate in a separate `COUNT`. EF Core runs it as a second query.
- **Search:** `LIKE '%q%'` on `Product.Name` and `Brand.Name` is not sargable. This is acceptable at the catalogue scale. Full-Text Search (supported in Azure SQL) is the escalation path, and it would need its own ADR.

## 5. Concurrency

- Admin updates use `rowversion` optimistic concurrency. A stale `RowVersion` raises `DbUpdateConcurrencyException`, which is mapped to 409 `concurrency_conflict`.
- **Future stock decrement** (checkout) must use an atomic conditional update rather than read-modify-write:

```sql
UPDATE catalog.ProductVariant
SET StockQuantity = StockQuantity - @qty
WHERE Id = @id AND StockQuantity >= @qty
```

  Zero affected rows means insufficient stock.

## 6. Migration Plan and Risks

| Item | Detail |
|---|---|
| Initial migration | `InitialCatalog` (P1-02). It creates schema `catalog`, all catalog tables, constraints and indexes, and seeds `Concentration`. It contains no destructive operations. |
| Second migration | `AddAdminIdentity` (P4-02). It creates schema `identity` and the Identity tables, and seeds the `Catalog.Admin` role. It is additive. |
| Test databases | Created and dropped per test run on LocalDB or a local SQL Server, and on the SQL Server service container in CI (ADR-0013). Migrations are applied with `MigrateAsync`; `EnsureCreated` is never used, so migrations are tested. |
| Location | `src/UTperfume.Infrastructure/Migrations`, startup project `src/UTperfume.Api` |
| Review | CI produces `dotnet ef migrations script --idempotent`. The Reviewer checks it for `DROP`, column narrowing, NOT NULL added without a default on populated tables, and renames. |
| Upgrade | Non-production: a CI job applies the idempotent script after build. Production: a manual, approved step (CLAUDE.md safety rules). The app never auto-migrates outside Development. |
| Rollback | The initial migration in non-production can be undone by dropping and recreating the database. After go-live, the policy is forward-fix plus expand/contract for breaking changes, with Azure SQL point-in-time restore as the last resort. Down migrations are kept but not relied on. |
| Risks | (1) `HasData` changes to `Concentration` produce UPDATE/DELETE statements, so they must be reviewed. (2) The filtered unique index needs the correct `SET` options; EF handles this. (3) Changing `VolumeMl` precision later is a narrowing risk. (4) Adding NOT NULL columns to populated tables later needs a default or a backfill. (5) `bigint` money: a future move to multi-currency with minor units means changing to `decimal`, which widens the column and is not destructive, but needs a code change across DTOs. (6) The Identity `HasData` role seed must keep a fixed `ConcurrencyStamp`, or every new migration will contain an UPDATE for it. |
| Dev data | A sample catalogue is loaded by a Development-only seeder, not by migrations, so it never reaches production. |

## 7. Explicitly Deferred Tables

The following tables are deferred:

- `StockMovement` (ADR-0005)
- `ProductSlugHistory` (Q-08)
- `AuditLog` (admin sign-in and write events go to structured logs for now)
- Orphan-blob tracking (ADR-0014)
- `Customer` and related
- `Cart` / `CartItem`
- `Order` / `OrderLine` (with SKU, name, volume and price snapshots and an FK to `ProductVariant`)
- `Payment`, `Shipment`
- `Promotion`
- `Review`
- `WishlistItem`

None of them requires changes to the tables above, other than new foreign keys pointing at them.
