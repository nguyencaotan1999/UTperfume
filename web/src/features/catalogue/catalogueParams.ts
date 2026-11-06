import type { ProductListQuery } from '../../api/queries/products'
import type { ProductSort } from '../../api/types'

/**
 * Catalogue state as it appears in the URL (AC-S2). Values are kept as the raw text of the query string, so a shared
 * link restores exactly what it says and an invalid value reaches the API, which answers 400 with a field error.
 * Query parameter names match the API (architecture 5.2).
 */
export interface CatalogueParams {
  q: string
  brand: string[]
  category: string[]
  concentration: string[]
  volume: string[]
  minPrice: string
  maxPrice: string
  sort: string
  page: string
  pageSize: string
}

export const DEFAULT_SORT: ProductSort = 'newest'
export const DEFAULT_PAGE_SIZE = 24
export const PAGE_SIZES = [12, 24, 48] as const
export const MAX_FILTER_PRICE = 10_000_000_000

export const SORT_OPTIONS: ReadonlyArray<{ value: ProductSort; label: string }> = [
  { value: 'newest', label: 'Newest' },
  { value: 'price_asc', label: 'Price: low to high' },
  { value: 'price_desc', label: 'Price: high to low' },
  { value: 'name_asc', label: 'Name: A to Z' },
  { value: 'name_desc', label: 'Name: Z to A' },
]

export const emptyParams: CatalogueParams = {
  q: '',
  brand: [],
  category: [],
  concentration: [],
  volume: [],
  minPrice: '',
  maxPrice: '',
  sort: '',
  page: '',
  pageSize: '',
}

const listKeys = ['brand', 'category', 'concentration', 'volume'] as const
const textKeys = ['q', 'minPrice', 'maxPrice', 'sort', 'page', 'pageSize'] as const

export function parseParams(search: URLSearchParams): CatalogueParams {
  const params: CatalogueParams = { ...emptyParams }
  for (const key of textKeys) params[key] = search.get(key) ?? ''
  for (const key of listKeys) params[key] = search.getAll(key).filter((value) => value !== '')
  return params
}

/** Writes the state back to a query string. Defaults and empty values are omitted so URLs stay canonical. */
export function toSearchParams(params: CatalogueParams): URLSearchParams {
  const search = new URLSearchParams()
  if (params.q.trim() !== '') search.set('q', params.q.trim())
  for (const key of listKeys) for (const value of params[key]) search.append(key, value)
  if (params.minPrice !== '') search.set('minPrice', params.minPrice)
  if (params.maxPrice !== '') search.set('maxPrice', params.maxPrice)
  if (params.sort !== '' && params.sort !== DEFAULT_SORT) search.set('sort', params.sort)
  if (params.pageSize !== '' && params.pageSize !== String(DEFAULT_PAGE_SIZE)) search.set('pageSize', params.pageSize)
  if (params.page !== '' && params.page !== '1') search.set('page', params.page)
  return search
}

/**
 * Canonical query for the page: the same state without `sort` (ADR-0006), so sorted views of one filter set
 * share one canonical URL.
 */
export function canonicalSearch(params: CatalogueParams): string {
  const search = toSearchParams({ ...params, sort: '' }).toString()
  return search === '' ? '' : `?${search}`
}

const WHOLE_NUMBER = /^\d+$/
const DECIMAL_NUMBER = /^\d+(\.\d+)?$/

/**
 * Converts the URL state to the typed E2 query. Numeric text becomes a number; anything else is passed through
 * unchanged on purpose, so the server stays the single authority on validity and returns the 400 field errors.
 */
export function toQuery(params: CatalogueParams): ProductListQuery {
  const query: Record<string, unknown> = {}
  if (params.q.trim() !== '') query.q = params.q.trim()
  for (const key of ['brand', 'category', 'concentration'] as const) {
    if (params[key].length > 0) query[key] = params[key]
  }
  if (params.volume.length > 0) {
    query.volume = params.volume.map((v) => (DECIMAL_NUMBER.test(v) ? Number(v) : v))
  }
  for (const key of ['minPrice', 'maxPrice', 'page', 'pageSize'] as const) {
    if (params[key] !== '') query[key] = WHOLE_NUMBER.test(params[key]) ? Number(params[key]) : params[key]
  }
  if (params.sort !== '') query.sort = params.sort
  return query as ProductListQuery
}

/** Applies a filter change. Changing any filter, sort or page size sends the user back to page 1. */
export function applyChange(
  current: CatalogueParams,
  patch: Partial<CatalogueParams>,
  options: { keepPage?: boolean } = {},
): CatalogueParams {
  return { ...current, ...patch, page: options.keepPage ? (patch.page ?? current.page) : (patch.page ?? '') }
}

export function toggleValue(values: string[], value: string, same: (a: string, b: string) => boolean = (a, b) => a === b) {
  return values.some((v) => same(v, value)) ? values.filter((v) => !same(v, value)) : [...values, value]
}

export const sameVolume = (a: string, b: string) => Number(a) === Number(b)

export function parsePositiveInt(text: string, fallback: number): number {
  return WHOLE_NUMBER.test(text) && Number(text) >= 1 ? Number(text) : fallback
}

// Client-side checks. They only guide the user before a request is sent; the API validates again.

export function validateSearchText(text: string): string | null {
  const trimmed = text.trim()
  if (trimmed === '') return null
  if (trimmed.length < 2) return 'Enter at least 2 characters.'
  if (trimmed.length > 100) return 'Enter at most 100 characters.'
  return null
}

/** Whole dong only: digits, no decimals, signs or separators. Empty means no limit. */
export function validatePrice(text: string): string | null {
  const trimmed = text.trim()
  if (trimmed === '') return null
  if (!WHOLE_NUMBER.test(trimmed)) return 'Enter whole dong using digits only (no decimals or separators).'
  if (Number(trimmed) > MAX_FILTER_PRICE) return 'Enter at most 10,000,000,000.'
  return null
}

export function validatePriceRange(min: string, max: string): string | null {
  if (validatePrice(min) || validatePrice(max) || min.trim() === '' || max.trim() === '') return null
  return Number(min) > Number(max) ? 'The minimum price must not be above the maximum price.' : null
}
