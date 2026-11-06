import type { ProductDetail } from '../../api/types'

const tiers = [
  { key: 'top', title: 'Top notes' },
  { key: 'heart', title: 'Heart notes' },
  { key: 'base', title: 'Base notes' },
] as const

/** The fragrance pyramid in order: top, heart, base (ADR-0003). Tiers without notes are left out. */
export function NotesPyramid({ notes }: { notes: ProductDetail['notes'] }) {
  const visible = tiers.filter((tier) => notes[tier.key].length > 0)
  if (visible.length === 0) return null

  return (
    <section aria-labelledby="notes-title" className="section">
      <h2 id="notes-title">Fragrance notes</h2>
      <ol className="notes">
        {visible.map((tier) => (
          <li key={tier.key} className={`notes__tier notes__tier--${tier.key}`}>
            <h3>{tier.title}</h3>
            <ul>
              {notes[tier.key].map((note) => (
                <li key={note.slug}>{note.name}</li>
              ))}
            </ul>
          </li>
        ))}
      </ol>
    </section>
  )
}
