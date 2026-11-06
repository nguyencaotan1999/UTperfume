import { useQuery } from '@tanstack/react-query'
import { client } from '../client'
import { unwrap } from '../problem'
import { filterKeys } from './keys'

/** E9: the brands, categories, concentrations, volumes and price range that visible products actually use. */
export function useCatalogFilters() {
  return useQuery({
    queryKey: filterKeys.all,
    queryFn: ({ signal }) => unwrap(client.GET('/api/v1/catalog/filters', { signal })),
  })
}
