/** Client route helpers (ADR-0006). Query parameter names match the API. */
export const routes = {
  home: '/',
  catalogue: '/perfumes',
  product: (slug: string) => `/perfumes/${encodeURIComponent(slug)}`,
  brands: '/brands',
  brand: (slug: string) => `/brands/${encodeURIComponent(slug)}`,
  category: (slug: string) => `/categories/${encodeURIComponent(slug)}`,
} as const

/** A banner link must be a site-relative path (BR-23). Anything else is ignored, even if the API sent it. */
export function safeInternalPath(path: string | null | undefined): string | null {
  return path && path.startsWith('/') && !path.startsWith('//') && !path.includes('\\') ? path : null
}
