import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { client } from '../client'
import { unwrap } from '../problem'
import type { paths } from '../schema'
import { productKeys } from './keys'

/** The E2 query string as the API declares it. */
export type ProductListQuery = NonNullable<paths['/api/v1/products']['get']['parameters']['query']>

/**
 * E2 product search. `keepPreviousData` keeps the grid on screen while the next page or filter loads, and the
 * query `signal` cancels a request that a newer filter has made obsolete.
 */
export function useProducts(query: ProductListQuery) {
  return useQuery({
    queryKey: productKeys.list(query),
    queryFn: ({ signal }) => unwrap(client.GET('/api/v1/products', { params: { query }, signal })),
    placeholderData: keepPreviousData,
  })
}

/** E3 product detail. A 404 (unknown or hidden slug) arrives as an ApiError with status 404 and is not retried. */
export function useProduct(slug: string) {
  return useQuery({
    queryKey: productKeys.detail(slug),
    queryFn: ({ signal }) => unwrap(client.GET('/api/v1/products/{slug}', { params: { path: { slug } }, signal })),
  })
}

/** E4 related products (default limit, at most 8). */
export function useRelatedProducts(slug: string) {
  return useQuery({
    queryKey: productKeys.related(slug),
    queryFn: ({ signal }) => unwrap(client.GET('/api/v1/products/{slug}/related', { params: { path: { slug } }, signal })),
  })
}
