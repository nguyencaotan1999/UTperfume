import { routes } from '../../shared/lib/routes'
import { Seo } from '../../shared/seo/Seo'
import { canonicalSearch, parsePositiveInt } from './catalogueParams'
import { CatalogueResults } from './CatalogueResults'
import { useCatalogueParams } from './useCatalogueParams'

/** Catalogue page at /perfumes (E2 and E9). */
export function CataloguePage() {
  const { params } = useCatalogueParams()
  const currentPage = parsePositiveInt(params.page, 1)

  return (
    <section className="page">
      <Seo
        title={currentPage > 1 ? `Perfumes – page ${currentPage}` : 'Perfumes'}
        description="Browse perfumes by brand, category, concentration, volume and price."
        canonicalPath={`${routes.catalogue}${canonicalSearch(params)}`}
      />
      <h1>Perfumes</h1>
      <CatalogueResults />
    </section>
  )
}
