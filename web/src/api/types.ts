import type { components } from './schema'

/** Public API shapes, re-exported from the generated schema so components never import the schema directly. */
export type Schemas = components['schemas']
export type ProductSummary = Schemas['ProductSummary']
export type BrandSummary = Schemas['BrandSummary']
export type CategorySummary = Schemas['CategorySummary']
export type BannerDto = Schemas['BannerDto']
export type HomeResponse = Schemas['HomeResponse']
export type CatalogFilters = Schemas['CatalogFilters']
export type PagedProducts = Schemas['PagedResultOfProductSummary']
export type AvailabilityStatus = Schemas['AvailabilityStatus']
export type ProductSort = 'newest' | 'price_asc' | 'price_desc' | 'name_asc' | 'name_desc'
export type ProductDetail = Schemas['ProductDetail']
export type VariantDto = Schemas['VariantDto']
export type ProductImageDto = Schemas['ProductImageDto']
export type BrandDetail = Schemas['BrandDetail']
export type CategoryDetail = Schemas['CategoryDetail']
