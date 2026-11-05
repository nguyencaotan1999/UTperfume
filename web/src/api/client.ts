import createClient, { type Middleware } from 'openapi-fetch'
import type { paths } from './schema'

export const XSRF_COOKIE_NAME = '__Host-utp-xsrf'
export const XSRF_HEADER_NAME = 'X-XSRF-TOKEN'

const UNSAFE_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE'])

export function readCookie(name: string, cookieString: string = document.cookie): string | undefined {
  for (const part of cookieString.split(';')) {
    const index = part.indexOf('=')
    if (index < 0) continue
    if (part.slice(0, index).trim() === name) {
      try {
        return decodeURIComponent(part.slice(index + 1).trim())
      } catch {
        return undefined
      }
    }
  }
  return undefined
}

/** Adds the antiforgery header from the XSRF cookie on unsafe methods (architecture 7.2). */
export function createXsrfMiddleware(getCookie: (name: string) => string | undefined = readCookie): Middleware {
  return {
    onRequest({ request }) {
      if (UNSAFE_METHODS.has(request.method.toUpperCase())) {
        const token = getCookie(XSRF_COOKIE_NAME)
        if (token) request.headers.set(XSRF_HEADER_NAME, token)
      }
      return request
    },
  }
}

export function createApiClient(
  options: { baseUrl?: string; fetch?: typeof fetch; getCookie?: (name: string) => string | undefined } = {},
) {
  const client = createClient<paths>({
    // Absolute same-origin base: Request() rejects relative URLs outside a browser (jsdom, Node).
    baseUrl: options.baseUrl ?? globalThis.location?.origin ?? '',
    credentials: 'same-origin',
    // Resolve fetch per call (not at creation) so tests and polyfills can replace it.
    fetch: options.fetch ?? ((request: Request) => globalThis.fetch(request)),
  })
  client.use(createXsrfMiddleware(options.getCookie))
  return client
}

export const client = createApiClient()
