# ADR-0006: SEO URL and slug strategy

- Status: Accepted (user approval 2026-11-05). Slug redirects (Q-08) are still open.
- Date: 2026-11-05
- Related: requirements BR-25, BR-26, AC-S1 to AC-S4; architecture §5.1

## Context

Public pages must have SEO-friendly, shareable URLs. The brief suggests `/perfumes/{slug}`.

## Decision

- **Client routes:**
  - `/perfumes` (catalogue)
  - `/perfumes/{slug}` (product)
  - `/brands`, `/brands/{slug}`
  - `/categories/{slug}`

  `/perfumes/{slug}` is kept. It is short, keyword-rich and stable, and it does not embed the brand. Embedding the brand (`/{brand}/{product}`) would break URLs when brand data is corrected.
- **Product slugs** include brand and concentration, e.g. `dior-sauvage-edp`. This keeps them globally unique and descriptive.
- **Slug format:**
  - lowercase ASCII `^[a-z0-9]+(?:-[a-z0-9]+)*$`, at most 200 characters
  - unique per entity table
  - generated from the name with diacritic transliteration (including Vietnamese `đ` → `d`)
  - editable by admin
  - enforced by the app and by a DB CHECK constraint plus a unique index
- **Filter and page state** goes in query parameters using slugs/codes: `/perfumes?brand=dior&concentration=EDP&page=2`.
- **Canonical URLs:**
  - product pages are canonical to `/perfumes/{slug}`
  - catalogue pages with filters are canonical to themselves without `sort`
- `sitemap.xml` is served by the API, and `robots.txt` is a static file.
- API lookups use the same slugs: `/api/v1/products/{slug}`.

## Consequences

- When a slug changes, old links return 404 until slug-history redirects are added (Q-08).
- The SPA is client-rendered, so indexing relies on JavaScript-capable crawlers, the sitemap and correct meta tags. SSR or prerendering would need a stack decision (risk R-01).
