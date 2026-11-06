import { useQuery } from '@tanstack/react-query'
import { client } from '../client'
import { unwrap } from '../problem'
import { brandKeys } from './keys'

/** E5 active brands with their visible product counts. */
export function useBrands() {
  return useQuery({
    queryKey: brandKeys.list(),
    queryFn: ({ signal }) => unwrap(client.GET('/api/v1/brands', { signal })),
  })
}

/** E6 one active brand. An unknown or inactive slug is a 404 ApiError. */
export function useBrand(slug: string) {
  return useQuery({
    queryKey: brandKeys.detail(slug),
    queryFn: ({ signal }) => unwrap(client.GET('/api/v1/brands/{slug}', { params: { path: { slug } }, signal })),
  })
}
