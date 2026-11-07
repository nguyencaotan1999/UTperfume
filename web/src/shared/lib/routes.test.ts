import { describe, expect, it } from 'vitest'
import { routes, safeAdminReturnPath, safeInternalPath } from './routes'

describe('safeAdminReturnPath (AC-A-41)', () => {
  it('keeps admin paths', () => {
    expect(safeAdminReturnPath('/admin/products')).toBe('/admin/products')
    expect(safeAdminReturnPath('/admin/products?page=2')).toBe('/admin/products?page=2')
  })

  it('falls back to the admin home for anything else', () => {
    for (const value of [undefined, null, 42, '', '/perfumes', '//evil.test', 'https://evil.test', '/admin/login', '/admin//x', '/admin\\x', '/administrator']) {
      expect(safeAdminReturnPath(value)).toBe('/admin')
    }
  })
})

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
