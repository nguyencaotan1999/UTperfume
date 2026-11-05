# ADR-0005: Stock on the variant, availability status only in public, ledger deferred

- Status: Accepted (user approval 2026-11-05). Revised: exact quantity is admin-only, and a `LowStock` status is added.
- Date: 2026-11-05
- Related: `docs/architecture/0002-database-model.md` §3.5, §5; BR-08, BR-28; AC-P2, AC-P2b

## Context

The storefront must show availability per variant. The user decided that **exact stock quantities must not be shown publicly**. There are no orders, reservations or warehouses yet.

## Decision

- **Balance:** `ProductVariant.StockQuantity int NOT NULL CHECK (>= 0)` holds the current balance, for a single location. It is **admin-only**. It is never selected into a public DTO and never appears in public OpenAPI schemas.
- **Public status:** public responses carry `availability` only. It is derived in the query using a threshold `T`:

  | Status | Condition |
  |---|---|
  | `OutOfStock` | `StockQuantity = 0` |
  | `LowStock` | `1 <= StockQuantity <= T` |
  | `InStock` | `StockQuantity > T` |

- **Threshold location:** `T` is the application setting **`Catalog:LowStockThreshold`**, default 5, an integer >= 1 validated at startup (`IOptions<CatalogOptions>` with `ValidateOnStart`).
  - It is set in `appsettings.json`, and in Azure it can be overridden with an App Service setting.
  - It is global for now. A per-variant override column is a possible later, additive change.
  - Changing the threshold needs no migration. Its effect becomes visible after the 60 s public cache expires.
- **Listing flag:** product listings expose `inStock: bool`, which is true when any active variant is `InStock` or `LowStock`.
- **Concurrency:** admin stock edits use `rowversion` and are logged with the user ID.
- **No `StockMovement` ledger now.** It is introduced together with orders, along with an atomic conditional decrement.

Rejected alternatives:

- **Exact quantity in public responses:** rejected by the user.
- **A capped display such as "10+":** it still leaks quantity at low levels.
- **The threshold as a database column now:** there is no requirement for per-product thresholds.

## Consequences

- Competitors cannot read exact stock levels. "Low stock" still signals scarcity, which is intended.
- Integration and contract tests must assert that no quantity field appears in any public response (AC-P2b).
- There is no stock history until the ledger is added.
