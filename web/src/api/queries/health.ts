import { useQuery } from '@tanstack/react-query'
import { client } from '../client'
import { unwrap } from '../problem'
import { healthKeys } from './keys'

/** Sample hook proving the typed client + TanStack Query wiring. */
export function useHealth() {
  return useQuery({
    queryKey: healthKeys.status(),
    queryFn: ({ signal }) => unwrap(client.GET('/api/v1/health', { signal })),
  })
}
