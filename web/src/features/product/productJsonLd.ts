import type { AvailabilityStatus, ProductDetail } from '../../api/types'

const availabilityUrl: Record<AvailabilityStatus, string> = {
  InStock: 'https://schema.org/InStock',
  LowStock: 'https://schema.org/LimitedAvailability',
  OutOfStock: 'https://schema.org/OutOfStock',
}

/** The best status across the variants: in stock beats low stock beats out of stock. */
function overallAvailability(statuses: readonly AvailabilityStatus[]): AvailabilityStatus {
  if (statuses.includes('InStock')) return 'InStock'
  if (statuses.includes('LowStock')) return 'LowStock'
  return 'OutOfStock'
}

/**
 * schema.org Product for the product page (architecture 5.2). Prices are the integer VND values from the API and the
 * currency is "VND". No stock quantity is ever included, only the availability status.
 */
export function buildProductJsonLd(product: ProductDetail, pageUrl: string): object {
  const prices = product.variants.map((v) => v.price)
  const image = product.images.map((i) => i.url)

  return {
    '@context': 'https://schema.org',
    '@type': 'Product',
    name: product.name,
    url: pageUrl,
    ...(product.seo.metaDescription ?? product.shortDescription
      ? { description: product.seo.metaDescription ?? product.shortDescription }
      : {}),
    ...(image.length > 0 ? { image } : {}),
    brand: { '@type': 'Brand', name: product.brand.name },
    ...(product.variants.length > 0
      ? {
          offers: {
            '@type': 'AggregateOffer',
            priceCurrency: 'VND',
            lowPrice: Math.min(...prices),
            highPrice: Math.max(...prices),
            offerCount: product.variants.length,
            availability: availabilityUrl[overallAvailability(product.variants.map((v) => v.availability))],
            offers: product.variants.map((variant) => ({
              '@type': 'Offer',
              sku: variant.sku,
              price: variant.price,
              priceCurrency: 'VND',
              availability: availabilityUrl[variant.availability],
              url: pageUrl,
            })),
          },
        }
      : {}),
  }
}
