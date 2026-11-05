# ADR-0004: Price filter and sort use the product "from-price" (whole VND)

- Status: Accepted (user approval 2026-11-05). Revised for VND.
- Date: 2026-11-05
- Related: requirements BR-10 to BR-15; AC-C4, AC-C5, AC-C5b, AC-C8, AC-C12

## Context

Prices live on variants, but listings show products. Filtering, sorting and the displayed price must be consistent. If they are not, a product shown as "From 1.500.000 ₫" could match `minPrice=2000000`, which confuses shoppers.

The user confirmed that the store currency is **VND, in whole dong with no decimals**.

## Decision

- **From-price** = `MIN(Price)` over the product's **active** variants.
  - When a volume filter is present, only variants with a selected volume count.
  - Out-of-stock active variants are included. Inactive variants are excluded.
- **Filter and sort:** the `minPrice`/`maxPrice` filter (inclusive) and the `price_asc`/`price_desc` sorts use the from-price.
- **Display:** the listing shows the from-price and the compare-at price of the same variant. On a tie, the lowest volume wins.
- **Units:** all amounts are whole VND.
  - Database: `bigint`.
  - C# and API: `long`, which appears in OpenAPI as `integer` (`int64`).
  - `minPrice` and `maxPrice` are integers from 0 to 10,000,000,000. A decimal value returns 400.
  - `currency` is always `"VND"`.
  - The UI formats prices with `vi-VN` and zero fraction digits.
- **Discount:** `floor((OriginalPrice - Price) * 100 / OriginalPrice)`, in integer arithmetic.
- **Computation:** the from-price is computed in the query and is not stored.

Rejected alternatives:

- **"Any variant in range"**: it can contradict the displayed price.
- **A stored `MinPrice` now**: premature.
- **`decimal(18,2)`**: VND has no minor units, so it would invite meaningless fractional values.

## Consequences

- Behaviour is simple to explain and to test.
- Price inputs in the UI must reject decimals.
- The escalation path for performance is a denormalised `Product.MinActivePrice bigint`.
- Multi-currency would need a type change. It is out of scope.
