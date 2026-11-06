import { useRelatedProducts } from '../../api/queries/products'
import { ProductCard } from '../../shared/ui/ProductCard'

/**
 * Related products (E4). They are an extra: while loading, when empty and when the call fails the section is left
 * out, so a problem here never hides the product itself.
 */
export function RelatedProducts({ slug }: { slug: string }) {
  const related = useRelatedProducts(slug)
  const items = related.data ?? []
  if (items.length === 0) return null

  return (
    <section aria-labelledby="related-title" className="section">
      <h2 id="related-title">You may also like</h2>
      <ul className="grid">
        {items.map((product) => (
          <li key={product.id}>
            <ProductCard product={product} />
          </li>
        ))}
      </ul>
    </section>
  )
}
