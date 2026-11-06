import { Link } from 'react-router'
import type { ProductSummary } from '../../api/types'
import { routes } from '../lib/routes'
import { AvailabilityBadge } from './AvailabilityBadge'
import { Price } from './Price'

/**
 * Catalogue card. The list API reports only whether anything is in stock, so the card shows "In stock" or
 * "Out of stock"; the three-way status (including low stock) belongs to the variant list on the product page.
 */
export function ProductCard({ product }: { product: ProductSummary }) {
  const image = product.primaryImage
  return (
    <article className="card">
      <Link to={routes.product(product.slug)} className="card__link">
        <div className="card__media">
          {image ? (
            <img src={image.url} alt={image.altText ?? product.name} loading="lazy" />
          ) : (
            <div className="card__placeholder" role="img" aria-label={`${product.name}, no image available`} />
          )}
          {product.isNew && <span className="badge badge--new card__new">New</span>}
        </div>
        <p className="card__brand">{product.brand.name}</p>
        <h3 className="card__name">{product.name}</h3>
      </Link>
      <p className="card__meta">
        {product.concentration.name}
        {product.volumesMl.length > 0 && <> · {product.volumesMl.map((v) => `${v} ml`).join(', ')}</>}
      </p>
      <Price price={product.fromPrice} originalPrice={product.fromOriginalPrice} prefix="From" />
      <AvailabilityBadge status={product.inStock ? 'InStock' : 'OutOfStock'} />
    </article>
  )
}
