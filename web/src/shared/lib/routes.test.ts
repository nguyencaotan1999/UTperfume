import { describe, expect, it } from 'vitest'
import { routes, safeInternalPath } from './routes'

describe('routes', () => {
  it('builds product, brand and category links', () => {
    expect(routes.product('dior-sauvage-edp')).toBe('/perfumes/dior-sauvage-edp')
    expect(routes.brand('dior')).toBe('/brands/dior')
    expect(routes.category('men')).toBe('/categories/men')
  })
})

describe('safeInternalPath', () => {
  it.each(['/perfumes', '/perfumes?brand=dior', '/'])('keeps the site-relative path %s', (path) => {
    expect(safeInternalPath(path)).toBe(path)
  })

  it.each(['//evil.example', 'https://evil.example', 'javascript:alert(1)', '/\\evil', '', null, undefined])(
    'drops %s',
    (path) => {
      expect(safeInternalPath(path)).toBeNull()
    },
  )
})
