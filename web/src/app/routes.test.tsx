import { screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { mockApi, problemResponse } from '../test/fetchMock'
import { makeDetail, makeFilters, makePage } from '../test/fixtures'
import { renderWithProviders } from '../test/renderWithProviders'
import { App } from './App'

afterEach(() => vi.unstubAllGlobals())

describe('client routes (architecture 5.1)', () => {
  it('routes /perfumes/:slug to the product page', async () => {
    mockApi({
      '/api/v1/products/sauvage': () => makeDetail(),
      '/api/v1/products/sauvage/related': () => [],
    })
    renderWithProviders(<App />, { route: '/perfumes/sauvage' })

    expect(await screen.findByRole('heading', { level: 1, name: 'Sauvage' })).toBeInTheDocument()
  })

  it('routes /brands, /brands/:slug and /categories/:slug', async () => {
    mockApi({
      '/api/v1/brands': () => [],
      '/api/v1/brands/dior': () => ({ slug: 'dior', name: 'Dior', description: null, logoUrl: null, productCount: 0 }),
      '/api/v1/categories/men': () => ({ slug: 'men', name: 'Men', description: null, imageUrl: null, productCount: 0 }),
      '/api/v1/products': () => makePage([]),
      '/api/v1/catalog/filters': () => makeFilters(),
    })

    const brands = renderWithProviders(<App />, { route: '/brands' })
    expect(await screen.findByRole('heading', { level: 1, name: 'Brands' })).toBeInTheDocument()
    brands.unmount()

    const brand = renderWithProviders(<App />, { route: '/brands/dior' })
    expect(await screen.findByRole('heading', { level: 1, name: 'Dior' })).toBeInTheDocument()
    brand.unmount()

    renderWithProviders(<App />, { route: '/categories/men' })
    expect(await screen.findByRole('heading', { level: 1, name: 'Men' })).toBeInTheDocument()
  })

  it('shows the not found page inside the layout for an unknown product slug', async () => {
    mockApi({ '/api/v1/products/ghost': () => problemResponse(404) })
    renderWithProviders(<App />, { route: '/perfumes/ghost' })

    expect(await screen.findByRole('heading', { level: 1, name: 'Page not found' })).toBeInTheDocument()
    expect(screen.getByRole('navigation', { name: 'Main' })).toBeInTheDocument()
  })

  it('links to the brand list from the main navigation', () => {
    mockApi({})
    renderWithProviders(<App />, { route: '/nowhere' })

    expect(screen.getByRole('link', { name: 'Brands' })).toHaveAttribute('href', '/brands')
  })
})
