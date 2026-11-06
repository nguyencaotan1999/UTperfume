import { Link, useParams } from 'react-router'
import { useProduct } from '../../api/queries/products'
import { ApiError } from '../../api/problem'
import { NotFoundPage } from '../../app/NotFoundPage'
import { routes } from '../../shared/lib/routes'
import { Seo } from '../../shared/seo/Seo'
import { Spinner } from '../../shared/ui/Spinner'
import { NotesPyramid } from './NotesPyramid'
import { ProductGallery } from './ProductGallery'
import { RelatedProducts } from './RelatedProducts'
import { VariantSelector } from './VariantSelector'
import { buildProductJsonLd } from './productJsonLd'

/** Product page (E3 and E4) at /perfumes/:slug. An unknown or hidden slug shows the not found page (AC-S4). */
export function ProductPage() {
  const { slug = '' } = useParams()
  const query = useProduct(slug)

  if (query.isPending) return <Spinner label="Loading the perfume" />

  if (query.error instanceof ApiError && query.error.status === 404) return <NotFoundPage />

  if (query.isError) {
    return (
      <div role="alert" className="notice notice--error">
        <p>The perfume could not be loaded.</p>
        <button type="button" className="button" onClick={() => void query.refetch()}>
          Try again
        </button>
      </div>
    )
  }

  const product = query.data
  const path = routes.product(product.slug)
  const pageUrl = new URL(path, window.location.origin).toString()
  const paragraphs = (product.description ?? '').split(/\n{2,}/).filter((p) => p.trim() !== '')

  return (
    <article className="page product">
      <Seo
        fullTitle={product.seo.metaTitle}
        description={product.seo.metaDescription ?? product.shortDescription ?? undefined}
        canonicalPath={path}
        jsonLd={[buildProductJsonLd(product, pageUrl)]}
      />

      <div className="product__layout">
        <ProductGallery productName={product.name} images={product.images} />

        <div className="product__info">
          <p className="product__brand">
            <Link to={routes.brand(product.brand.slug)}>{product.brand.name}</Link>
          </p>
          <h1>{product.name}</h1>
          <p className="product__concentration">{product.concentration.name}</p>
          {product.shortDescription && <p className="product__short">{product.shortDescription}</p>}

          <VariantSelector variants={product.variants} />

          {product.categories.length > 0 && (
            <p className="product__categories">
              Categories:{' '}
              {product.categories.map((category, index) => (
                <span key={category.slug}>
                  {index > 0 && ', '}
                  <Link to={routes.category(category.slug)}>{category.name}</Link>
                </span>
              ))}
            </p>
          )}
        </div>
      </div>

      {paragraphs.length > 0 && (
        <section aria-labelledby="description-title" className="section">
          <h2 id="description-title">About this perfume</h2>
          {paragraphs.map((text, index) => (
            <p key={index}>{text}</p>
          ))}
        </section>
      )}

      <NotesPyramid notes={product.notes} />
      <RelatedProducts slug={product.slug} />
    </article>
  )
}
