# ADR-0001: Product + ProductVariant model

- Status: Accepted (user approval 2026-11-05). The tester and gift-set question (Q-07) is still open.
- Date: 2026-11-05
- Related: `docs/architecture/0002-database-model.md` §3.4–3.5; requirements BR-04 to BR-06

## Context

A perfume is sold in several sizes (for example Dior Sauvage EDP in 30, 60 and 100 ml). Each size has its own SKU, price, compare-at price, stock and availability. The descriptive data is shared across sizes: name, brand, description, images, notes and categories.

Future cart and order lines must reference a specific purchasable unit.

## Decision

Use a two-level model:

- **Product** holds the shared, descriptive data.
- **ProductVariant** is the purchasable unit: SKU, `VolumeMl`, `Price`, `OriginalPrice`, `StockQuantity`, `IsActive`.
- A product has 1..N variants, with volume unique per product.
- Variants are deactivated rather than deleted (FK `NO ACTION`).

Rejected alternatives:

- **One table with a row per size.** This duplicates the descriptive data and breaks the "one product page" concept.
- **A generic attribute/option system (EAV, option sets).** This is speculative. Volume is the only varying dimension today.

## Consequences

- Listings aggregate over variants (from-price, available volumes, stock). See ADR-0004.
- Future order lines get a stable target, `ProductVariant.Id`.
- If another varying dimension appears (tester, gift set), revisit the uniqueness on `(ProductId, VolumeMl)` (Q-07).
