import { useQuery } from '@tanstack/react-query'
import { client } from '../client'
import { unwrap } from '../problem'
import { categoryKeys } from './keys'

/** E8 one active category. An unknown or inactive slug is a 404 ApiError. */
export function useCategory(slug: string) {
  return useQuery({
    queryKey: categoryKeys.detail(slug),
    queryFn: ({ signal }) => unwrap(client.GET('/api/v1/categories/{slug}', { params: { path: { slug } }, signal })),
  })
}
