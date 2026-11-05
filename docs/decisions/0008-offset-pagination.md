# ADR-0008: Offset (page-number) pagination with a standard envelope

- Status: Accepted (user approval 2026-11-05)
- Date: 2026-11-05
- Related: requirements BR-17; AC-C1, AC-C2, AC-C8

## Context

The catalogue needs server-side pagination. The UI shows numbered pages, and SEO needs crawlable `?page=N` URLs. The catalogue is moderate in size (assumption A-06).

## Decision

- **Offset pagination** with `page` (>= 1, default 1) and `pageSize` (1–60, default 24).
- **Response envelope:** `{ items, page, pageSize, totalItems, totalPages }`.
- **Deterministic ordering:** every sort has `Id` as its final tie-breaker.
- **Out-of-range pages** return 200 with empty `items` and correct totals.
- **Invalid values** return 400 ValidationProblemDetails.
- Small bounded lists (brands, categories, home sections, related) are not paginated.

Rejected alternative: **keyset/cursor pagination.** It cannot jump to page N and gives worse SEO URLs. It is unnecessary at this scale.

## Consequences

- Each page needs a separate `COUNT` query.
- Deep offsets get slower on very large tables. That is acceptable here.
- The same envelope is reused by future admin lists.
