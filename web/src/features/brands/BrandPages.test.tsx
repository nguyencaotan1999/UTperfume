import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Route, Routes } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { NotFoundPage } from '../../app/NotFoundPage'
import { jsonResponse, mockApi, problemResponse } from '../../test/fetchMock'
import { makeFilters, makePage, makeProduct } from '../../test/fixtures'
import { LocationProbe } from '../../test/LocationProbe'
import { renderWithProviders } from '../../test/renderWithProviders'
import { CategoryPage } from '../categories/CategoryPage'
import { BrandListPage } from './BrandListPage'
import { BrandPage } from './BrandPage'

afterEach(() => vi.unstubAllGlobals())

const brand = { slug: 'dior', name: 'Dior', description: 'House of Dior.', logoUrl: 'https://images.test/dior.png', productCount: 12 }
const category = { slug: 'men', name: 'Men', description: 'Fragrances for men.', imageUrl: null, productCount: 1 }

const renderRoutes = (route: string) =>
  renderWithProviders(
    <>
      <Routes>
        <Route path="brands" element={<BrandListPage />} />
        <Route path="brands/:slug" element={<BrandPage />} />
        <Route path="categories/:slug" element={<CategoryPage />} />
        <Route path="*" element={<NotFoundPage />} />
      </Routes>
      <LocationProbe />
    </>,
    { route },
  )

const location = () => screen.getByTestId('location').textContent ?? ''

const brandApi = (productsHandler?: (url: URL) => Response | object) =>
  mockApi({
    '/api/v1/brands/dior': () => brand,
    '/api/v1/products': (url) =>
      productsHandler ? productsHandler(url) : makePage([makeProduct({ id: 1 }), makeProduct({ id: 2 })], { totalItems: 50, totalPages: 3 }),
    '/api/v1/catalog/filters': () => makeFilters(),
  })

describe('BrandPage (E6, AC-B2, AC-B3)', () => {
  it('shows the brand header: name, logo, description and product count', async () => {
    brandApi()
    renderRoutes('/brands/dior')

    expect(await screen.findByRole('heading', { level: 1, name: 'Dior' })).toBeInTheDocument()
    expect(screen.getByRole('img', { name: 'Dior logo' })).toHaveAttribute('src', 'https://images.test/dior.png')
    expect(screen.getByText('House of Dior.')).toBeInTheDocument()
    expect(screen.getByText('12 perfumes', { selector: 'p.page__count' })).toBeInTheDocument()
  })

  it('reuses the catalogue list and sends the brand with every request', async () => {
    const api = brandApi()
    renderRoutes('/brands/dior?sort=price_asc&concentration=EDP')

    expect(await screen.findAllByRole('heading', { level: 3 })).toHaveLength(2)
    const query = new URL(api.forPath('/api/v1/products')[0]!.url).searchParams
    expect(query.getAll('brand')).toEqual(['dior'])
    expect(query.get('sort')).toBe('price_asc')
    expect(query.getAll('concentration')).toEqual(['EDP'])
    expect(screen.getByLabelText('Sort by')).toHaveValue('price_asc')
    expect(screen.getByRole('group', { name: 'Concentration' })).toBeInTheDocument()
  })

  it('does not offer the brand filter again, and the path brand wins over a brand in the URL', async () => {
    const api = brandApi()
    renderRoutes('/brands/dior?brand=chanel')

    await screen.findAllByRole('heading', { level: 3 })

    expect(screen.queryByRole('group', { name: 'Brand' })).not.toBeInTheDocument()
    expect(screen.getByRole('group', { name: 'Category' })).toBeInTheDocument()
    expect(new URL(api.forPath('/api/v1/products')[0]!.url).searchParams.getAll('brand')).toEqual(['dior'])
  })

  it('regression: the path brand wins, so there is no chip and the canonical link for a different brand in the URL', async () => {
    brandApi()
    renderRoutes('/brands/dior?brand=chanel')
    await screen.findAllByRole('heading', { level: 3 })

    expect(screen.queryByText(/Brand: chanel/i)).not.toBeInTheDocument()
    expect(document.head.querySelector('link[rel="canonical"]')).toHaveAttribute('href', `${window.location.origin}/brands/dior`)
  })

  it('paginates through the URL and keeps the brand in the path', async () => {
    const api = brandApi((url) => makePage([makeProduct({ id: 1 })], { totalItems: 50, totalPages: 3, page: Number(url.searchParams.get('page') ?? 1) }))
    renderRoutes('/brands/dior')
    await screen.findAllByRole('heading', { level: 3 })

    await userEvent.click(screen.getByRole('link', { name: 'Page 2' }))

    expect(location()).toBe('/brands/dior?page=2')
    await waitFor(() => expect(api.queries('/api/v1/products').at(-1)).toContain('page=2'))
    expect(api.queries('/api/v1/products').at(-1)).toContain('brand=dior')
  })

  it('writes a filter to the URL below the brand path', async () => {
    brandApi()
    renderRoutes('/brands/dior')
    await screen.findAllByRole('heading', { level: 3 })

    await userEvent.click(await screen.findByLabelText('Eau de Parfum'))

    expect(location()).toBe('/brands/dior?concentration=EDP')
  })

  it('sets the title, description and a canonical link without the sort', async () => {
    brandApi()
    renderRoutes('/brands/dior?sort=price_asc&page=2')
    await screen.findByRole('heading', { level: 1, name: 'Dior' })

    expect(document.title).toBe('Dior perfumes – page 2 | UTperfume')
    expect(document.head.querySelector('meta[name="description"]')).toHaveAttribute('content', 'House of Dior.')
    expect(document.head.querySelector('link[rel="canonical"]')).toHaveAttribute('href', `${window.location.origin}/brands/dior?page=2`)
  })

  it('shows the not found page for an unknown or inactive brand (AC-B2, AC-S4)', async () => {
    mockApi({ '/api/v1/brands/ghost': () => problemResponse(404) })
    renderRoutes('/brands/ghost')

    expect(await screen.findByRole('heading', { level: 1, name: 'Page not found' })).toBeInTheDocument()
  })

  it('shows an empty state for a brand without products', async () => {
    brandApi(() => makePage([], { totalItems: 0 }))
    renderRoutes('/brands/dior')

    expect(await screen.findByText('No perfumes match your search.')).toBeInTheDocument()
  })

  it('shows an error with a retry when the brand cannot be loaded', async () => {
    let calls = 0
    mockApi({
      '/api/v1/brands/dior': () => (++calls === 1 ? problemResponse(500) : jsonResponse(brand)),
      '/api/v1/products': () => makePage([makeProduct({ id: 1 })]),
      '/api/v1/catalog/filters': () => makeFilters(),
    })
    renderRoutes('/brands/dior')

    expect(await screen.findByText('The brand could not be loaded.')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }))

    expect(await screen.findByRole('heading', { level: 1, name: 'Dior' })).toBeInTheDocument()
  })

  it('never shows a quantity', async () => {
    brandApi()
    const { container } = renderRoutes('/brands/dior')
    await screen.findAllByRole('heading', { level: 3 })

    expect(container.textContent?.toLowerCase()).not.toContain('quantity')
  })
})

describe('CategoryPage (E8, AC-K1)', () => {
  const categoryApi = () =>
    mockApi({
      '/api/v1/categories/men': () => category,
      '/api/v1/products': () => makePage([makeProduct({ id: 7, name: 'For Men' })], { totalItems: 30, totalPages: 2 }),
      '/api/v1/catalog/filters': () => makeFilters(),
    })

  it('shows the category header and its products, sending the category with the request', async () => {
    const api = categoryApi()
    renderRoutes('/categories/men')

    expect(await screen.findByRole('heading', { level: 1, name: 'Men' })).toBeInTheDocument()
    expect(screen.getByText('1 perfume', { selector: 'p.page__count' })).toBeInTheDocument()
    expect(screen.getByText('Fragrances for men.')).toBeInTheDocument()
    expect(await screen.findByRole('heading', { level: 3, name: 'For Men' })).toBeInTheDocument()
    expect(new URL(api.forPath('/api/v1/products')[0]!.url).searchParams.getAll('category')).toEqual(['men'])
    expect(screen.queryByRole('group', { name: 'Category' })).not.toBeInTheDocument()
    expect(screen.getByRole('group', { name: 'Brand' })).toBeInTheDocument()
  })

  it('regression: the path category wins, so there is no chip and the canonical link for a different category in the URL', async () => {
    categoryApi()
    renderRoutes('/categories/men?category=women')
    await screen.findByRole('heading', { level: 3, name: 'For Men' })

    expect(screen.queryByText(/Category: women/i)).not.toBeInTheDocument()
    expect(document.head.querySelector('link[rel="canonical"]')).toHaveAttribute('href', `${window.location.origin}/categories/men`)
  })

  it('paginates through the URL below the category path', async () => {
    categoryApi()
    renderRoutes('/categories/men')
    await screen.findByRole('heading', { level: 3, name: 'For Men' })

    await userEvent.click(screen.getByRole('link', { name: 'Page 2' }))

    expect(location()).toBe('/categories/men?page=2')
  })

  it('shows the not found page for an unknown or inactive category', async () => {
    mockApi({ '/api/v1/categories/ghost': () => problemResponse(404) })
    renderRoutes('/categories/ghost')

    expect(await screen.findByRole('heading', { level: 1, name: 'Page not found' })).toBeInTheDocument()
  })

  it('sets the title and canonical link', async () => {
    categoryApi()
    renderRoutes('/categories/men')
    await screen.findByRole('heading', { level: 1, name: 'Men' })

    expect(document.title).toBe('Men perfumes | UTperfume')
    expect(document.head.querySelector('link[rel="canonical"]')).toHaveAttribute('href', `${window.location.origin}/categories/men`)
  })
})

describe('BrandListPage (E5, AC-B1)', () => {
  it('lists the brands with their product counts and links to their pages', async () => {
    mockApi({
      '/api/v1/brands': () => [
        { slug: 'chanel', name: 'Chanel', logoUrl: null, productCount: 3 },
        { slug: 'dior', name: 'Dior', logoUrl: null, productCount: 1 },
        { slug: 'empty', name: 'Empty House', logoUrl: null, productCount: 0 },
      ],
    })
    renderRoutes('/brands')

    const list = await screen.findByRole('list', { name: 'Brands' })
    const links = within(list).getAllByRole('link')
    expect(links.map((l) => l.getAttribute('href'))).toEqual(['/brands/chanel', '/brands/dior', '/brands/empty'])
    expect(links[0]).toHaveTextContent('3 perfumes')
    expect(links[1]).toHaveTextContent('1 perfume')
    expect(links[2]).toHaveTextContent('0 perfumes')
    expect(screen.getByRole('heading', { level: 1, name: 'Brands' })).toBeInTheDocument()
  })

  it('shows an empty state and an error state', async () => {
    mockApi({ '/api/v1/brands': () => [] })
    const first = renderRoutes('/brands')
    expect(await screen.findByText('There are no brands yet.')).toBeInTheDocument()
    first.unmount()

    mockApi({ '/api/v1/brands': () => problemResponse(500) })
    renderRoutes('/brands')
    expect(await screen.findByRole('alert')).toHaveTextContent('could not be loaded')
  })
})
