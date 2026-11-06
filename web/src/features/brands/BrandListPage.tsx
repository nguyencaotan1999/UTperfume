import { Link } from 'react-router'
import { useBrands } from '../../api/queries/brands'
import { perfumeCount } from '../../shared/lib/plural'
import { routes } from '../../shared/lib/routes'
import { Seo } from '../../shared/seo/Seo'
import { Spinner } from '../../shared/ui/Spinner'

/** Brand list (E5) at /brands: every active brand with its visible product count (AC-B1). */
export function BrandListPage() {
  const brands = useBrands()

  return (
    <section className="page">
      <Seo title="Brands" description="Browse perfumes by brand." canonicalPath={routes.brands} />
      <h1>Brands</h1>

      {brands.isPending && <Spinner label="Loading brands" />}

      {brands.isError && (
        <div role="alert" className="notice notice--error">
          <p>The brands could not be loaded.</p>
          <button type="button" className="button" onClick={() => void brands.refetch()}>
            Try again
          </button>
        </div>
      )}

      {brands.data && brands.data.length === 0 && <p className="empty">There are no brands yet.</p>}

      {brands.data && brands.data.length > 0 && (
        <ul className="tiles" aria-label="Brands">
          {brands.data.map((brand) => (
            <li key={brand.slug}>
              <Link to={routes.brand(brand.slug)}>
                {brand.logoUrl && <img src={brand.logoUrl} alt="" loading="lazy" />}
                <span>{brand.name}</span>
                <span className="tiles__count">{perfumeCount(brand.productCount)}</span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
