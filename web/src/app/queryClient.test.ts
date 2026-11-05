import { describe, expect, it } from 'vitest'
import { ApiError } from '../api/problem'
import { createQueryClient, shouldRetry } from './queryClient'

describe('QueryClient defaults', () => {
  it('uses the architecture defaults', () => {
    const options = createQueryClient().getDefaultOptions().queries
    expect(options?.staleTime).toBe(60_000)
    expect(options?.refetchOnWindowFocus).toBe(false)
  })

  it('never retries 4xx', () => {
    expect(shouldRetry(0, new ApiError(404, 'x'))).toBe(false)
    expect(shouldRetry(0, new ApiError(400, 'x'))).toBe(false)
  })

  it('retries 5xx and network errors once', () => {
    expect(shouldRetry(0, new ApiError(503, 'x'))).toBe(true)
    expect(shouldRetry(0, new TypeError('network'))).toBe(true)
    expect(shouldRetry(1, new ApiError(503, 'x'))).toBe(false)
  })
})
