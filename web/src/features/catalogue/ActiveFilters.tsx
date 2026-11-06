import type { CatalogFilters } from '../../api/types'
import { formatVnd } from '../../shared/lib/formatVnd'
import { sameVolume, toggleValue, type CatalogueParams } from './catalogueParams'

interface ActiveFiltersProps {
  params: CatalogueParams
  filters: CatalogFilters | undefined
  onChange: (patch: Partial<CatalogueParams>) => void
}

/** Removable chips for every filter in the URL, including values the API no longer lists. */
export function ActiveFilters({ params, filters, onChange }: ActiveFiltersProps) {
  const chips: Array<{ key: string; label: string; remove: Partial<CatalogueParams> }> = []

  if (params.q.trim() !== '') chips.push({ key: 'q', label: `Search: ${params.q.trim()}`, remove: { q: '' } })

  const lists: Array<[keyof Pick<CatalogueParams, 'brand' | 'category' | 'concentration'>, string, Record<string, string>]> = [
    ['brand', 'Brand', Object.fromEntries((filters?.brands ?? []).map((b) => [b.slug, b.name]))],
    ['category', 'Category', Object.fromEntries((filters?.categories ?? []).map((c) => [c.slug, c.name]))],
    ['concentration', 'Concentration', Object.fromEntries((filters?.concentrations ?? []).map((c) => [c.code, c.name]))],
  ]
  for (const [key, title, names] of lists) {
    for (const value of params[key]) {
      chips.push({
        key: `${key}:${value}`,
        label: `${title}: ${names[value] ?? value}`,
        remove: { [key]: toggleValue(params[key], value) },
      })
    }
  }

  for (const value of params.volume) {
    chips.push({
      key: `volume:${value}`,
      label: `Volume: ${value} ml`,
      remove: { volume: toggleValue(params.volume, value, sameVolume) },
    })
  }

  const price = (text: string) => (/^\d+$/.test(text) ? formatVnd(Number(text)) : text)
  if (params.minPrice !== '') chips.push({ key: 'min', label: `From ${price(params.minPrice)}`, remove: { minPrice: '' } })
  if (params.maxPrice !== '') chips.push({ key: 'max', label: `Up to ${price(params.maxPrice)}`, remove: { maxPrice: '' } })

  if (chips.length === 0) return null

  return (
    <ul className="chips" aria-label="Active filters">
      {chips.map((chip) => (
        <li key={chip.key}>
          <button type="button" className="chip" onClick={() => onChange(chip.remove)}>
            {chip.label} <span aria-hidden="true">×</span>
            <span className="visually-hidden"> (remove filter)</span>
          </button>
        </li>
      ))}
    </ul>
  )
}
