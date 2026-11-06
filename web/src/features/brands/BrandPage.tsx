import { useParams } from 'react-router'
import { useBrand } from '../../api/queries/brands'
import { ApiError } from '../../api/problem'
import { NotFoundPage } from '../../app/NotFoundPage'
import { perfumeCount } from '../../shared/lib/plural'
import { routes } from '../../shared/lib/routes'
import { Seo } from '../../shared/seo/Seo'
import { Spinner } from '../../shared/ui/Spinner'
import { canonicalSearch, parsePositiveInt } from '../catalogue/catalogueParams'
import { CatalogueResults } from '../catalogue/CatalogueResults'
import { useCatalogueParams } from '../catalogue/useCatalogueParams'

/** Brand page (E6) at /brands/:slug, listing the brand's products with the shared catalogue list (AC-B3). */
export function BrandPage() {
  const { slug = '' } = useParams()
  const brand = useBrand(slug)
  const { params } = useCatalogueParams()

  if (brand.isPending) return <Spinner label="Loading the brand" />
  if (brand.error instanceof ApiError && brand.error.status === 404) return <NotFoundPage />
  if (brand.isError) {
    return (
      <div role="alert" className="notice notice--error">
        <p>The brand could not be loaded.</p>
        <button type="button" className="button" onClick={() => void brand.refetch()}>
          Try again
        </button>
      </div>
    )
  }

  const { data } = brand
  const page = parsePositiveInt(params.page, 1)

  return (
    <section className="page">
      <Seo
        title={page > 1 ? `${data.name} perfumes – page ${page}` : `${data.name} perfumes`}
        description={data.description ?? `Perfumes by ${data.name}.`}
        canonicalPath={`${routes.brand(data.slug)}${canonicalSearch({ ...params, brand: [] })}`}
      />
      <header className="page__header">
        {data.logoUrl && <img className="page__logo" src={data.logoUrl} alt={`${data.name} logo`} />}
        <div>
          <h1>{data.name}</h1>
          <p className="page__count">{perfumeCount(data.productCount)}</p>
          {data.description && <p>{data.description}</p>}
        </div>
      </header>
      <CatalogueResults fixed={{ brand: data.slug }} />
    </section>
  )
}
