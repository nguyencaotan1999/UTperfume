import { vi } from 'vitest'

export type ApiHandler = (url: URL, request: Request) => Response | object | Promise<Response | object>

export const jsonResponse = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })

export const problemResponse = (status: number, body: Record<string, unknown> = {}) =>
  new Response(JSON.stringify({ status, title: 'Error', traceId: 't', ...body }), {
    status,
    headers: { 'content-type': 'application/problem+json' },
  })

/**
 * Replaces `fetch` for the typed client. Routes are matched on the URL path. The request objects are kept so tests
 * can assert the query string and whether a request was cancelled.
 */
export function mockApi(routes: Record<string, ApiHandler>) {
  const requests: Request[] = []
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: Request) => {
      requests.push(input)
      const url = new URL(input.url)
      const handler = routes[url.pathname]
      if (!handler) return problemResponse(404)
      const result = await handler(url, input)
      return result instanceof Response ? result : jsonResponse(result)
    }),
  )
  return {
    requests,
    forPath: (path: string) => requests.filter((r) => new URL(r.url).pathname === path),
    queries: (path: string) => requests.filter((r) => new URL(r.url).pathname === path).map((r) => new URL(r.url).search),
  }
}
