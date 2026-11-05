import { describe, expect, it } from 'vitest'
import { ApiError, toApiError, unwrap } from './problem'

function result<T>(status: number, body: { data?: T; error?: unknown }) {
  return Promise.resolve({ ...body, response: new Response(null, { status }) })
}

describe('toApiError', () => {
  it('maps ProblemDetails fields', () => {
    const error = toApiError({ title: 'Conflict', detail: 'Slug exists', code: 'duplicate_slug', traceId: 'abc' }, 409)

    expect(error).toBeInstanceOf(ApiError)
    expect(error.status).toBe(409)
    expect(error.message).toBe('Slug exists')
    expect(error.code).toBe('duplicate_slug')
    expect(error.traceId).toBe('abc')
  })

  it('falls back to the title, then to a generic message', () => {
    expect(toApiError({ title: 'Not Found' }, 404).message).toBe('Not Found')
    expect(toApiError({}, 500).message).toBe('Request failed with status 500')
  })

  it('maps validation errors by field', () => {
    const error = toApiError({ title: 'Invalid', errors: { minPrice: ['Must be an integer'], bad: 'x' } }, 400)

    expect(error.fieldErrors).toEqual({ minPrice: ['Must be an integer'] })
  })

  it.each([null, undefined, 'text', 42, ['a']])('tolerates a non-object body (%s)', (body) => {
    const error = toApiError(body, 502)

    expect(error.status).toBe(502)
    expect(error.fieldErrors).toEqual({})
  })
})

describe('unwrap', () => {
  it('returns data on success', async () => {
    await expect(unwrap(result(200, { data: { status: 'ok' } }))).resolves.toEqual({ status: 'ok' })
  })

  it('throws ApiError built from ProblemDetails on failure', async () => {
    const promise = unwrap(result(404, { error: { title: 'Not Found', status: 404, traceId: 't1' } }))

    await expect(promise).rejects.toMatchObject({ name: 'ApiError', status: 404, traceId: 't1' })
  })

  it('treats an empty 204 as success', async () => {
    await expect(unwrap(result(204, {}))).resolves.toBeUndefined()
  })
})
