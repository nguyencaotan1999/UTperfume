# ADR-0002: Concentration is a lookup table referenced by Product

- Status: Accepted (user approval 2026-11-05). The seed list of concentrations still needs business confirmation.
- Date: 2026-11-05
- Related: `docs/architecture/0002-database-model.md` §3.3; BR-04, BR-09

## Context

Concentration (Parfum, Extrait, EDP, EDT, EDC, Eau Fraîche…) is used for filtering and display. It could be:

- a C# enum or a lookup table, and
- held on the product or on the variant.

In the market, "Sauvage EDT" and "Sauvage EDP" are different formulas, with different notes, descriptions and often different bottles.

## Decision

1. **Lookup table** `catalog.Concentration` with columns `Id`, `Code`, `Name`, `SortOrder` and `IsActive`. It is seeded through a migration.
   - Reason: display names, sort order and new values (e.g. "Elixir", "Parfum Intense") can be managed as data without a code release.
   - The public API exposes the stable `Code` (e.g. `EDP`) as the filter value.
2. **Concentration belongs to Product**, not to the variant. EDT and EDP of the same line are separate products with separate slugs, e.g. `dior-sauvage-edt` and `dior-sauvage-edp`.

Rejected alternatives:

- **Enum:** adding a value would need a deploy, and display names would have to live in code.
- **Concentration on the variant:** this would mix different formulas on one page, and notes and descriptions would no longer be shared truthfully.

## Consequences

- The concentration filter is a simple FK filter on Product.
- Grouping a "fragrance line" across concentrations (e.g. a "Sauvage family" link) is not modelled. It could be added later with a nullable `ProductLine` reference.
- `HasData` seed changes must be reviewed in migration scripts.
