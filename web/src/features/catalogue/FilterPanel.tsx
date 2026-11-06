import { useId, useState, type FormEvent } from 'react'
import type { CatalogFilters } from '../../api/types'
import { formatVnd } from '../../shared/lib/formatVnd'
import {
  sameVolume,
  toggleValue,
  validatePrice,
  validatePriceRange,
  validateSearchText,
  type CatalogueParams,
} from './catalogueParams'

type FieldErrors = Readonly<Record<string, readonly string[]>>

interface FilterPanelProps {
  params: CatalogueParams
  filters: CatalogFilters | undefined
  filtersFailed: boolean
  /** Field errors from a 400 response, keyed by query parameter name. */
  fieldErrors: FieldErrors
  onChange: (patch: Partial<CatalogueParams>) => void
  onClear: () => void
  /** Filters already fixed by the page (the brand on a brand page), so they are not offered again. */
  hideGroups?: ReadonlyArray<'brand' | 'category'>
}

export function FilterPanel({
  params,
  filters,
  filtersFailed,
  fieldErrors,
  onChange,
  onClear,
  hideGroups = [],
}: FilterPanelProps) {
  const hasActive =
    params.q !== '' ||
    params.minPrice !== '' ||
    params.maxPrice !== '' ||
    params.brand.length + params.category.length + params.concentration.length + params.volume.length > 0

  return (
    <div className="filters">
      {/* The key re-creates the draft inputs when the URL changes underneath them (back button, shared link). */}
      <SearchAndPriceForm
        key={`${params.q}|${params.minPrice}|${params.maxPrice}`}
        params={params}
        filters={filters}
        fieldErrors={fieldErrors}
        onApply={onChange}
      />

      {filtersFailed && (
        <p role="alert" className="notice notice--error">
          The filter options could not be loaded. Searching and sorting still work.
        </p>
      )}

      {!hideGroups.includes('brand') && (
        <CheckGroup
          legend="Brand"
          name="brand"
          options={(filters?.brands ?? []).map((b) => ({ value: b.slug, label: b.name }))}
          selected={params.brand}
          errors={fieldErrors.brand}
          onChange={(brand) => onChange({ brand })}
        />
      )}
      {!hideGroups.includes('category') && (
        <CheckGroup
          legend="Category"
          name="category"
          options={(filters?.categories ?? []).map((c) => ({ value: c.slug, label: c.name }))}
          selected={params.category}
          errors={fieldErrors.category}
          onChange={(category) => onChange({ category })}
        />
      )}
      <CheckGroup
        legend="Concentration"
        name="concentration"
        options={(filters?.concentrations ?? []).map((c) => ({ value: c.code, label: c.name }))}
        selected={params.concentration}
        errors={fieldErrors.concentration}
        onChange={(concentration) => onChange({ concentration })}
      />
      <CheckGroup
        legend="Volume"
        name="volume"
        options={(filters?.volumesMl ?? []).map((v) => ({ value: String(v), label: `${v} ml` }))}
        selected={params.volume}
        errors={fieldErrors.volume}
        same={sameVolume}
        onChange={(volume) => onChange({ volume })}
      />

      {hasActive && (
        <button type="button" className="button button--link" onClick={onClear}>
          Clear all filters
        </button>
      )}
    </div>
  )
}

interface SearchAndPriceFormProps {
  params: CatalogueParams
  filters: CatalogFilters | undefined
  fieldErrors: FieldErrors
  onApply: (patch: Partial<CatalogueParams>) => void
}

function SearchAndPriceForm({ params, filters, fieldErrors, onApply }: SearchAndPriceFormProps) {
  const id = useId()
  const [q, setQ] = useState(params.q)
  const [minPrice, setMinPrice] = useState(params.minPrice)
  const [maxPrice, setMaxPrice] = useState(params.maxPrice)
  const [clientErrors, setClientErrors] = useState<Partial<Record<'q' | 'minPrice' | 'maxPrice', string>>>({})

  function submit(event: FormEvent) {
    event.preventDefault()
    const errors = {
      q: validateSearchText(q) ?? undefined,
      minPrice: validatePrice(minPrice) ?? validatePriceRange(minPrice, maxPrice) ?? undefined,
      maxPrice: validatePrice(maxPrice) ?? undefined,
    }
    setClientErrors(errors)
    if (errors.q || errors.minPrice || errors.maxPrice) return
    onApply({ q: q.trim(), minPrice: minPrice.trim(), maxPrice: maxPrice.trim() })
  }

  const error = (field: 'q' | 'minPrice' | 'maxPrice') => clientErrors[field] ?? fieldErrors[field]?.[0]
  const range = filters?.priceRange

  return (
    <form role="search" aria-label="Search and price" onSubmit={submit} noValidate className="filters__form">
      <Field
        id={`${id}-q`}
        label="Search"
        error={error('q')}
        input={{ type: 'search', value: q, onChange: setQ, placeholder: 'Name, brand or SKU', maxLength: 200 }}
      />
      <div className="filters__prices">
        <Field
          id={`${id}-min`}
          label="Minimum price (₫)"
          error={error('minPrice')}
          input={{
            type: 'text',
            inputMode: 'numeric',
            value: minPrice,
            onChange: setMinPrice,
            placeholder: range ? formatVnd(range.min) : undefined,
          }}
        />
        <Field
          id={`${id}-max`}
          label="Maximum price (₫)"
          error={error('maxPrice')}
          input={{
            type: 'text',
            inputMode: 'numeric',
            value: maxPrice,
            onChange: setMaxPrice,
            placeholder: range ? formatVnd(range.max) : undefined,
          }}
        />
      </div>
      <button type="submit" className="button">
        Apply
      </button>
    </form>
  )
}

interface FieldProps {
  id: string
  label: string
  error: string | undefined
  input: {
    type: 'search' | 'text'
    inputMode?: 'numeric'
    value: string
    onChange: (value: string) => void
    placeholder?: string
    maxLength?: number
  }
}

function Field({ id, label, error, input }: FieldProps) {
  const errorId = `${id}-error`
  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      <input
        id={id}
        type={input.type}
        inputMode={input.inputMode}
        autoComplete="off"
        value={input.value}
        placeholder={input.placeholder}
        maxLength={input.maxLength}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? errorId : undefined}
        onChange={(event) => input.onChange(event.target.value)}
      />
      {error && (
        <p id={errorId} role="alert" className="field__error">
          {error}
        </p>
      )}
    </div>
  )
}

interface CheckGroupProps {
  legend: string
  name: string
  options: ReadonlyArray<{ value: string; label: string }>
  selected: string[]
  errors?: readonly string[]
  same?: (a: string, b: string) => boolean
  onChange: (values: string[]) => void
}

function CheckGroup({ legend, name, options, selected, errors, same, onChange }: CheckGroupProps) {
  const errorId = useId()
  // A value in the URL that the API does not list (an old link) stays visible and removable.
  const known = new Set(options.map((o) => o.value))
  const extra = selected.filter((v) => !options.some((o) => (same ? same(o.value, v) : o.value === v)) && !known.has(v))
  const all = [...options, ...extra.map((value) => ({ value, label: value }))]
  if (all.length === 0 && !errors?.length) return null

  return (
    <fieldset className="filters__group" aria-describedby={errors?.length ? errorId : undefined}>
      <legend>{legend}</legend>
      {all.map((option) => {
        const inputId = `${name}-${option.value}`
        return (
          <div key={option.value} className="check">
            <input
              id={inputId}
              type="checkbox"
              checked={selected.some((v) => (same ? same(v, option.value) : v === option.value))}
              onChange={() => onChange(toggleValue(selected, option.value, same))}
            />
            <label htmlFor={inputId}>{option.label}</label>
          </div>
        )
      })}
      {errors?.length ? (
        <p id={errorId} role="alert" className="field__error">
          {errors[0]}
        </p>
      ) : null}
    </fieldset>
  )
}
