import { Link } from 'react-router'
import { pageWindow } from './pageWindow'

interface PaginationProps {
  page: number
  totalPages: number
  /** Builds the query string for a page, so every page is a real, shareable link (ADR-0008). */
  searchFor: (page: number) => string
}

export function Pagination({ page, totalPages, searchFor }: PaginationProps) {
  if (totalPages <= 1) return null

  return (
    <nav aria-label="Pagination" className="pagination">
      {page > 1 ? (
        <Link to={{ search: searchFor(page - 1) }} rel="prev">
          Previous
        </Link>
      ) : (
        <span aria-disabled="true">Previous</span>
      )}
      <ul>
        {pageWindow(page, totalPages).map((item, index) =>
          item === 'gap' ? (
            <li key={`gap-${index}`} aria-hidden="true">
              …
            </li>
          ) : (
            <li key={item}>
              <Link
                to={{ search: searchFor(item) }}
                aria-current={item === page ? 'page' : undefined}
                aria-label={`Page ${item}`}
              >
                {item}
              </Link>
            </li>
          ),
        )}
      </ul>
      {page < totalPages ? (
        <Link to={{ search: searchFor(page + 1) }} rel="next">
          Next
        </Link>
      ) : (
        <span aria-disabled="true">Next</span>
      )}
    </nav>
  )
}
