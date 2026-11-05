# ADR-0003: Fragrance notes as a reusable entity with a typed product link

- Status: Accepted (user approval 2026-11-05)
- Date: 2026-11-05
- Related: `docs/architecture/0002-database-model.md` §3.7–3.8

## Context

Product detail must show the fragrance pyramid: top, heart and base notes. Notes such as Bergamot or Vanilla repeat across many products. Filtering or browsing by note is a plausible future feature.

## Decision

- `catalog.FragranceNote` holds `Id`, `Name` and `Slug`. Name and slug are unique.
- `catalog.ProductNote` links a product to a note with these columns:
  - `ProductId`, `FragranceNoteId`
  - `NoteType tinyint` (1 = Top, 2 = Heart, 3 = Base)
  - `SortOrder`
- The primary key is `(ProductId, NoteType, FragranceNoteId)`.
- `NoteType` is a C# enum stored as `tinyint` with a CHECK constraint. The pyramid tiers are a fixed industry concept and need no data management.
- The API groups notes into `{ top, heart, base }`.

Rejected alternatives:

- **Free-text columns on Product (`TopNotes nvarchar`)** cannot be filtered and duplicate spellings.
- **A NoteType lookup table** is unnecessary for three fixed values.

## Consequences

- Admins pick existing notes or create new ones, which enforces consistent naming.
- A future "filter by note" feature only needs the existing `IX_ProductNote_FragranceNoteId` index.
- Some fragrances are described as "linear" with no pyramid. They are represented by placing all notes under one tier, by convention to agree with the business.
