import { QueryClient } from '@tanstack/react-query'
import { ApiError } from '../api/problem'

/** Retry only network or 5xx errors, at most once. 4xx errors are never retried (architecture 5.2). */
export function shouldRetry(failureCount: number, error: unknown): boolean {
  if (failureCount >= 1) return false
  if (error instanceof ApiError) return error.status >= 500
  return true
}

export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 60_000,
        refetchOnWindowFocus: false,
        retry: shouldRetry,
      },
    },
  })
}

export const queryClient = createQueryClient()
