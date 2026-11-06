import { useQuery } from '@tanstack/react-query'
import { client } from '../client'
import { unwrap } from '../problem'
import { homeKeys } from './keys'

/** E1: banners, featured, new arrivals, best sellers, popular brands and categories. */
export function useHome() {
  return useQuery({
    queryKey: homeKeys.all,
    queryFn: ({ signal }) => unwrap(client.GET('/api/v1/home', { signal })),
  })
}
