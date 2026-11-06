import { useMemo } from 'react'
import { Link } from 'react-router'
import { useCatalogFilters } from '../../api/queries/filters'
import { useProducts } from '../../api/queries/products'
import { ApiError } from '../../api/problem'
import { ProductCard } from '../../shared/ui/ProductCard'
import { Spinner } from '../../shared/ui/Spinner'
import {
  DEFAULT_PAGE_SIZE,
  DEFAULT_SORT,
  PAGE_SIZES,
  SORT_OPTIONS,
  toQuery,
  toSearchParams,
} from './catalogueParams'
import { ActiveFilters } from './ActiveFilters'
import { FilterPanel } from './FilterPanel'
import { Pagination } from './Pagination'
import { useCatalogueParams } from './useCatalogueParams'

const NO_ERRORS: Readonly<Record<string, readonly string[]>> = {}

/** A filter fixed by the page path, for example the brand on /brands/:slug. It is not part of the query string. */
export interface FixedFilter {
  brand?: string
  category?: string
}

/**
 * The shared product list with its filters, sort, page size and pagination (E2 and E9). The URL query string is the
 * only source of state (AC-S2). A page can fix a brand or category from its path; that filter is always sent and its
 * own filter group is hidden.
 */
export function CatalogueResults({ fixed }: { fixed?: FixedFilter }) {
  const { params, update, clear } = useCatalogueParams()
  const fixedBrand = fixed?.brand
  const fixedCategory = fixed?.category
  const query = useMemo(() => {
    const base = toQuery(params)
    return {
      ...base,
      ...(fixedBrand ? { brand: [fixedBrand] } : {}),
      ...(fixedCategory ? { category: [fixedCategory] } : {}),
    }
  }, [params, fixedBrand, fixedCategory])
  const products = useProducts(query)
  const filters = useCatalogFilters()

  const error = products.error
  const invalid = error instanceof ApiError && error.status === 400
  const fieldErrors = invalid ? error.fieldErrors : NO_ERRORS
  const searchFor = (page: number) => {
    const search = toSearchParams({ ...params, page: String(page) }).toString()
    return search === '' ? '' : `?${search}`
  }

  const sortValue = params.sort === '' ? DEFAULT_SORT : params.sort
  const pageSizeValue = params.pageSize === '' ? String(DEFAULT_PAGE_SIZE) : params.pageSize
  const data = products.data

  return (
    <div className="catalogue">
        <aside className="catalogue__filters" aria-label="Filters">
          <FilterPanel
            params={params}
            filters={filters.data}
            filtersFailed={filters.isError}
            fieldErrors={fieldErrors}
            onChange={(patch) => update(patch)}
            onClear={clear}
            hideGroups={[...(fixedBrand ? (['brand'] as const) : []), ...(fixedCategory ? (['category'] as const) : [])]}
          />
        </aside>

        <div className="catalogue__results" aria-busy={products.isPlaceholderData}>
          <ActiveFilters
            params={{ ...params, ...(fixedBrand ? { brand: [] } : {}), ...(fixedCategory ? { category: [] } : {}) }}
            filters={filters.data} onChange={(patch) => update(patch)} />

          <div className="toolbar">
            <p role="status" className="toolbar__count">
              {data ? `${data.totalItems} ${data.totalItems === 1 ? 'perfume' : 'perfumes'}` : ' '}
            </p>
            <div className="toolbar__controls">
              <div className="field field--inline">
                <label htmlFor="sort">Sort by</label>
                <select id="sort" value={sortValue} onChange={(event) => update({ sort: event.target.value })}>
                  {SORT_OPTIONS.every((o) => o.value !== sortValue) && <option value={sortValue}>{sortValue}</option>}
                  {SORT_OPTIONS.map((option) => (
                    <option key={option.value} value={option.value}>
                      {option.label}
                    </option>
                  ))}
                </select>
              </div>
              <div className="field field--inline">
                <label htmlFor="pageSize">Per page</label>
                <select id="pageSize" value={pageSizeValue} onChange={(event) => update({ pageSize: event.target.value })}>
                  {PAGE_SIZES.every((size) => String(size) !== pageSizeValue) && (
                    <option value={pageSizeValue}>{pageSizeValue}</option>
                  )}
                  {PAGE_SIZES.map((size) => (
                    <option key={size} value={size}>
                      {size}
                    </option>
                  ))}
                </select>
              </div>
            </div>
          </div>

          {(fieldErrors.sort || fieldErrors.page || fieldErrors.pageSize) && (
            <ul role="alert" className="field__error">
              {[...(fieldErrors.sort ?? []), ...(fieldErrors.page ?? []), ...(fieldErrors.pageSize ?? [])].map((message) => (
                <li key={message}>{message}</li>
              ))}
            </ul>
          )}

          {products.isPending && <Spinner label="Loading perfumes" />}

          {invalid && (
            <p role="alert" className="notice notice--error">
              Some of the search options are not valid. Check the highlighted fields.
            </p>
          )}

          {error && !invalid && (
            <div role="alert" className="notice notice--error">
              <p>The perfumes could not be loaded.</p>
              <button type="button" className="button" onClick={() => void products.refetch()}>
                Try again
              </button>
            </div>
          )}

          {data && data.items.length === 0 && (
            <div className="empty">
              {data.totalItems === 0 ? (
                <>
                  <p>No perfumes match your search.</p>
                  <button type="button" className="button" onClick={clear}>
                    Clear all filters
                  </button>
                </>
              ) : (
                <p>
                  There are no results on this page. <Link to={{ search: searchFor(1) }}>Go to the first page</Link>.
                </p>
              )}
            </div>
          )}

          {data && data.items.length > 0 && (
            <ul className="grid" aria-label="Perfumes">
              {data.items.map((product) => (
                <li key={product.id}>
                  <ProductCard product={product} />
                </li>
              ))}
            </ul>
          )}

          {data && <Pagination page={data.page} totalPages={data.totalPages} searchFor={searchFor} />}
        </div>
    </div>
  )
}
