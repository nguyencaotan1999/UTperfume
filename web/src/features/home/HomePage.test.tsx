import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { jsonResponse, mockApi, problemResponse } from '../../test/fetchMock'
import { makeHome, makeProduct } from '../../test/fixtures'
import { LocationProbe } from '../../test/LocationProbe'
import { plain } from '../../test/text'
import { renderWithProviders } from '../../test/renderWithProviders'
import { HomePage } from './HomePage'

afterEach(() => vi.unstubAllGlobals())

const renderHome = () =>
  renderWithProviders(
    <>
      <HomePage />
      <LocationProbe />
    </>,
  )

const section = (name: string) => screen.getByRole('region', { name })

describe('HomePage', () => {
  it('always shows the page heading and a link to the catalogue, whatever the data does', () => {
    mockApi({ '/api/v1/home': () => new Promise<never>(() => undefined) })

    renderHome()

    expect(screen.getByRole('heading', { level: 1, name: 'Find your signature fragrance' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Browse all perfumes' })).toHaveAttribute('href', '/perfumes')
    expect(screen.getByText('Loading the home page')).toBeInTheDocument()
  })

  it('shows every section with its products, brands and categories', async () => {
    mockApi({
      '/api/v1/home': () =>
        makeHome({
          banners: [
            { title: 'New season', subtitle: 'Fresh scents', imageUrl: 'https://images.test/b.png', altText: 'Season banner', linkUrl: '/perfumes' },
          ],
          featuredProducts: [makeProduct({ id: 1, name: 'Featured One' })],
          newArrivals: [makeProduct({ id: 2, name: 'New One', isNew: true })],
          bestSellers: [makeProduct({ id: 3, name: 'Best One' })],
          popularBrands: [{ slug: 'dior', name: 'Dior', logoUrl: null, productCount: 4 }],
          categories: [{ slug: 'men', name: 'Men', imageUrl: null, productCount: 7 }],
        }),
    })

    renderHome()

    expect(await screen.findByRole('heading', { level: 2, name: 'Featured perfumes' })).toBeInTheDocument()
    expect(within(section('Featured perfumes')).getByRole('heading', { name: 'Featured One', level: 3 })).toBeInTheDocument()
    expect(within(section('New arrivals')).getByRole('heading', { name: 'New One' })).toBeInTheDocument()
    expect(within(section('Best sellers')).getByRole('heading', { name: 'Best One' })).toBeInTheDocument()
    expect(within(section('Popular brands')).getByRole('link', { name: /Dior/ })).toHaveAttribute('href', '/brands/dior')
    expect(within(section('Popular brands')).getByRole('link', { name: /Dior/ })).toHaveTextContent('4 perfumes')
    expect(within(section('Shop by category')).getByRole('link', { name: /Men/ })).toHaveAttribute('href', '/categories/men')
    expect(within(section('Highlights')).getByRole('img', { name: 'Season banner' })).toBeInTheDocument()
    expect(within(section('Highlights')).getByRole('link')).toHaveAttribute('href', '/perfumes')
  })

  // AC-H4 as seen by the UI: every section is empty and says so.
  it('shows an empty state in every section when there is no data', async () => {
    mockApi({ '/api/v1/home': () => makeHome() })

    renderHome()

    expect(await screen.findByText('No featured perfumes yet.')).toBeInTheDocument()
    expect(screen.getByText('No new arrivals yet.')).toBeInTheDocument()
    expect(screen.getByText('No best sellers yet.')).toBeInTheDocument()
    expect(screen.getByText('No popular brands yet.')).toBeInTheDocument()
    expect(screen.getByText('No categories yet.')).toBeInTheDocument()
    expect(screen.getByText('No current promotions.')).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 1 })).toBeInTheDocument()
  })

  it('shows an empty state only for the sections that are empty', async () => {
    mockApi({ '/api/v1/home': () => makeHome({ featuredProducts: [makeProduct({ id: 1, name: 'Only Featured' })] }) })

    renderHome()

    expect(await screen.findByRole('heading', { name: 'Only Featured' })).toBeInTheDocument()
    expect(screen.queryByText('No featured perfumes yet.')).not.toBeInTheDocument()
    expect(screen.getByText('No new arrivals yet.')).toBeInTheDocument()
  })

  it('shows prices in dong and the availability status, never a quantity', async () => {
    mockApi({
      '/api/v1/home': () =>
        makeHome({
          featuredProducts: [
            makeProduct({ id: 1, fromPrice: 3_200_000, fromOriginalPrice: 4_000_000, inStock: true }),
            makeProduct({ id: 2, inStock: false }),
          ],
        }),
    })

    const { container } = renderHome()

    await screen.findByText('Featured perfumes')
    expect(plain(container.textContent)).toContain('3.200.000 ₫')
    expect(plain(container.textContent)).toContain('4.000.000 ₫')
    expect(screen.getByText('In stock')).toBeInTheDocument()
    expect(screen.getByText('Out of stock')).toBeInTheDocument()
    expect(container.textContent?.toLowerCase()).not.toContain('quantity')
    expect(container.textContent).not.toMatch(/\b\d+ (left|remaining|in stock)\b/i)
  })

  it('renders a banner without a link as plain content and ignores an unsafe link', async () => {
    mockApi({
      '/api/v1/home': () =>
        makeHome({
          banners: [
            { title: 'No link', subtitle: null, imageUrl: 'https://images.test/1.png', altText: 'one', linkUrl: null },
            { title: 'Unsafe', subtitle: null, imageUrl: 'https://images.test/2.png', altText: 'two', linkUrl: 'https://evil.example' },
          ],
        }),
    })

    renderHome()

    const banners = await screen.findByRole('region', { name: 'Highlights' })
    expect(within(banners).queryAllByRole('link')).toHaveLength(0)
    expect(within(banners).getByText('No link')).toBeInTheDocument()
    expect(within(banners).getByText('Unsafe')).toBeInTheDocument()
  })

  it('shows an error with a retry when the home data cannot be loaded', async () => {
    let calls = 0
    mockApi({
      '/api/v1/home': () => (++calls === 1 ? problemResponse(500) : jsonResponse(makeHome({ featuredProducts: [makeProduct({ id: 9, name: 'After retry' })] }))),
    })

    renderHome()

    expect(await screen.findByRole('alert')).toHaveTextContent('could not be loaded')
    expect(screen.getByRole('heading', { level: 1 })).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Try again' }))

    expect(await screen.findByRole('heading', { name: 'After retry' })).toBeInTheDocument()
  })

  it('labels the product count on brand and category tiles with a correct plural', async () => {
    mockApi({
      '/api/v1/home': () =>
        makeHome({
          popularBrands: [
            { slug: 'dior', name: 'Dior', logoUrl: null, productCount: 12 },
            { slug: 'solo', name: 'Solo', logoUrl: null, productCount: 1 },
          ],
          categories: [{ slug: 'men', name: 'Men', imageUrl: null, productCount: 0 }],
        }),
    })

    renderHome()

    const brands = await screen.findByRole('region', { name: 'Popular brands' })
    expect(within(brands).getByRole('link', { name: /Dior/ })).toHaveTextContent('12 perfumes')
    expect(within(brands).getByRole('link', { name: /Solo/ })).toHaveTextContent('1 perfume')
    expect(within(brands).getByRole('link', { name: /Solo/ })).not.toHaveTextContent('1 perfumes')
    expect(within(screen.getByRole('region', { name: 'Shop by category' })).getByRole('link', { name: /Men/ })).toHaveTextContent(
      '0 perfumes',
    )
  })

  it('opens the brand page', async () => {
    mockApi({
      '/api/v1/home': () => makeHome({ popularBrands: [{ slug: 'chanel', name: 'Chanel', logoUrl: null, productCount: 2 }] }),
    })

    renderHome()
    await userEvent.click(await screen.findByRole('link', { name: /Chanel/ }))

    expect(screen.getByTestId('location')).toHaveTextContent('/brands/chanel')
  })

  it('uses the typed home query only, with no other API call', async () => {
    const api = mockApi({ '/api/v1/home': () => makeHome() })

    renderHome()
    await screen.findByText('No categories yet.')

    expect(api.requests.map((r) => new URL(r.url).pathname)).toEqual(['/api/v1/home'])
  })

  it('sets the page title and canonical link', async () => {
    mockApi({ '/api/v1/home': () => makeHome() })

    renderHome()
    await screen.findByText('No categories yet.')

    expect(document.title).toContain('UTperfume')
    expect(document.head.querySelector('link[rel="canonical"]')).toHaveAttribute('href', `${window.location.origin}/`)
  })
})
