import { waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../problem'
import { renderHookWithProviders } from '../../test/renderWithProviders'
import { useHealth } from './health'

function json(status: number, body: unknown, contentType = 'application/json') {
  return new Response(JSON.stringify(body), { status, headers: { 'content-type': contentType } })
}

afterEach(() => vi.unstubAllGlobals())

describe('useHealth', () => {
  it('returns the health payload', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json(200, { status: 'ok' })))

    const { result } = renderHookWithProviders(() => useHealth())

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(result.current.data).toEqual({ status: 'ok' })
  })

  it('surfaces ProblemDetails as an ApiError', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => json(500, { title: 'An unexpected error occurred.', status: 500, traceId: 'x' }, 'application/problem+json')),
    )

    const { result } = renderHookWithProviders(() => useHealth())

    await waitFor(() => expect(result.current.isError).toBe(true))
    expect(result.current.error).toBeInstanceOf(ApiError)
    expect((result.current.error as ApiError).status).toBe(500)
  })
})
