import { describe, expect, it, vi } from 'vitest'
import { createApiClient, createXsrfMiddleware, readCookie } from './client'

describe('readCookie', () => {
  it('reads a named cookie among several', () => {
    expect(readCookie('b', 'a=1; b=two%20words; c=3')).toBe('two words')
  })

  it('returns undefined when absent or malformed', () => {
    expect(readCookie('x', 'a=1')).toBeUndefined()
    expect(readCookie('a', 'a=%E0%A4%A')).toBeUndefined()
  })

  it('does not match on a name suffix', () => {
    expect(readCookie('xsrf', 'my-xsrf=1')).toBeUndefined()
  })
})

describe('XSRF middleware', () => {
  const run = async (method: string, token: string | undefined) => {
    const middleware = createXsrfMiddleware(() => token)
    const request = new Request('http://localhost/api/v1/x', { method })
    const result = await middleware.onRequest!({ request } as Parameters<NonNullable<typeof middleware.onRequest>>[0])
    return (result as Request).headers.get('X-XSRF-TOKEN')
  }

  it.each(['POST', 'PUT', 'PATCH', 'DELETE'])('adds the header on %s', async (method) => {
    expect(await run(method, 'tok')).toBe('tok')
  })

  it.each(['GET', 'HEAD', 'OPTIONS'])('does not add the header on %s', async (method) => {
    expect(await run(method, 'tok')).toBeNull()
  })

  it('adds nothing when there is no cookie', async () => {
    expect(await run('POST', undefined)).toBeNull()
  })

  it('reads the __Host-utp-xsrf cookie by default', async () => {
    const getCookie = vi.fn(() => 'abc')
    const middleware = createXsrfMiddleware(getCookie)
    await middleware.onRequest!({ request: new Request('http://localhost/x', { method: 'POST' }) } as Parameters<
      NonNullable<typeof middleware.onRequest>
    >[0])

    expect(getCookie).toHaveBeenCalledWith('__Host-utp-xsrf')
  })
})

describe('createApiClient', () => {
  it('sends the XSRF header on an unsafe request through the real client', async () => {
    const fetchMock = vi.fn((...[, ]: [Request]) => Promise.resolve(new Response('{}', { status: 200, headers: { 'content-type': 'application/json' } })))
    const client = createApiClient({ baseUrl: 'http://localhost', fetch: fetchMock as unknown as typeof fetch, getCookie: () => 'cookie-token' })

    await client.POST('/api/v1/health' as never, {} as never)

    const sent = fetchMock.mock.calls[0]![0]
    expect(sent.headers.get('X-XSRF-TOKEN')).toBe('cookie-token')
  })
})
