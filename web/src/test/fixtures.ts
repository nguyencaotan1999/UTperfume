import type { CatalogFilters, HomeResponse, PagedProducts, ProductDetail, ProductSummary, VariantDto } from '../api/types'

export function makeProduct(overrides: Partial<ProductSummary> & { id?: number } = {}): ProductSummary {
  const id = overrides.id ?? 1
  return {
    id,
    slug: `product-${id}`,
    name: `Product ${id}`,
    brand: { slug: 'dior', name: 'Dior' },
    concentration: { code: 'EDP', name: 'Eau de Parfum' },
    primaryImage: { url: `https://images.test/products/${id}.png`, altText: `Product ${id} bottle` },
    fromPrice: 1_250_000,
    fromOriginalPrice: null,
    volumesMl: [50, 100],
    inStock: true,
    isNew: false,
    currency: 'VND',
    ...overrides,
  }
}

export function makePage(items: ProductSummary[], overrides: Partial<PagedProducts> = {}): PagedProducts {
  const pageSize = overrides.pageSize ?? 24
  const totalItems = overrides.totalItems ?? items.length
  return {
    items,
    page: 1,
    pageSize,
    totalItems,
    totalPages: totalItems === 0 ? 0 : Math.ceil(totalItems / pageSize),
    ...overrides,
  }
}

export function makeFilters(overrides: Partial<CatalogFilters> = {}): CatalogFilters {
  return {
    brands: [
      { slug: 'dior', name: 'Dior' },
      { slug: 'chanel', name: 'Chanel' },
    ],
    categories: [{ slug: 'men', name: 'Men' }],
    concentrations: [
      { code: 'EDP', name: 'Eau de Parfum' },
      { code: 'EDT', name: 'Eau de Toilette' },
    ],
    volumesMl: [30, 50, 100],
    priceRange: { min: 1_250_000, max: 8_900_000 },
    sortOptions: ['newest', 'price_asc', 'price_desc', 'name_asc', 'name_desc'],
    currency: 'VND',
    ...overrides,
  }
}

export function makeHome(overrides: Partial<HomeResponse> = {}): HomeResponse {
  return {
    banners: [],
    featuredProducts: [],
    newArrivals: [],
    bestSellers: [],
    popularBrands: [],
    categories: [],
    ...overrides,
  }
}

export function makeVariant(overrides: Partial<VariantDto> = {}): VariantDto {
  return {
    sku: 'SAU-50',
    volumeMl: 50,
    price: 2_000_000,
    originalPrice: null,
    discountPercent: null,
    availability: 'InStock',
    ...overrides,
  }
}

export function makeDetail(overrides: Partial<ProductDetail> = {}): ProductDetail {
  return {
    id: 1,
    slug: 'sauvage',
    name: 'Sauvage',
    brand: { slug: 'dior', name: 'Dior' },
    concentration: { code: 'EDP', name: 'Eau de Parfum' },
    shortDescription: 'Fresh and woody',
    description: 'First paragraph.\n\nSecond paragraph.',
    categories: [{ slug: 'men', name: 'Men' }],
    images: [
      { url: 'https://images.test/a.png', altText: 'Front of the bottle', isPrimary: true, width: 800, height: 600 },
      { url: 'https://images.test/b.png', altText: 'Side of the bottle', isPrimary: false, width: null, height: null },
    ],
    notes: {
      top: [{ slug: 'bergamot', name: 'Bergamot' }],
      heart: [{ slug: 'pepper', name: 'Pepper' }],
      base: [{ slug: 'amber', name: 'Amber' }],
    },
    variants: [makeVariant()],
    seo: { metaTitle: 'Sauvage – Dior | UTperfume', metaDescription: 'Fresh and woody' },
    publishedAtUtc: '2026-06-01T00:00:00Z',
    currency: 'VND',
    ...overrides,
  }
}
