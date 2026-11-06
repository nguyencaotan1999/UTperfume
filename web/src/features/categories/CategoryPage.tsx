import { useParams } from 'react-router'
import { useCategory } from '../../api/queries/categories'
import { ApiError } from '../../api/problem'
import { NotFoundPage } from '../../app/NotFoundPage'
import { perfumeCount } from '../../shared/lib/plural'
import { routes } from '../../shared/lib/routes'
import { Seo } from '../../shared/seo/Seo'
import { Spinner } from '../../shared/ui/Spinner'
import { canonicalSearch, parsePositiveInt } from '../catalogue/catalogueParams'
import { CatalogueResults } from '../catalogue/CatalogueResults'
import { useCatalogueParams } from '../catalogue/useCatalogueParams'

/** Category page (E8) at /categories/:slug, listing the category's products with the shared catalogue list. */
export function CategoryPage() {
  const { slug = '' } = useParams()
  const category = useCategory(slug)
  const { params } = useCatalogueParams()

  if (category.isPending) return <Spinner label="Loading the category" />
  if (category.error instanceof ApiError && category.error.status === 404) return <NotFoundPage />
  if (category.isError) {
    return (
      <div role="alert" className="notice notice--error">
        <p>The category could not be loaded.</p>
        <button type="button" className="button" onClick={() => void category.refetch()}>
          Try again
        </button>
      </div>
    )
  }

  const { data } = category
  const page = parsePositiveInt(params.page, 1)

  return (
    <section className="page">
      <Seo
        title={page > 1 ? `${data.name} perfumes – page ${page}` : `${data.name} perfumes`}
        description={data.description ?? `Browse ${data.name} perfumes.`}
        canonicalPath={`${routes.category(data.slug)}${canonicalSearch({ ...params, category: [] })}`}
      />
      <header className="page__header">
        {data.imageUrl && <img className="page__logo" src={data.imageUrl} alt="" />}
        <div>
          <h1>{data.name}</h1>
          <p className="page__count">{perfumeCount(data.productCount)}</p>
          {data.description && <p>{data.description}</p>}
        </div>
      </header>
      <CatalogueResults fixed={{ category: data.slug }} />
    </section>
  )
}
