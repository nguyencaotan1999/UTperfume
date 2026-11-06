import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useNavigate } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { jsonResponse, mockApi, problemResponse } from '../../test/fetchMock'
import { makeFilters, makePage, makeProduct } from '../../test/fixtures'
import { LocationProbe } from '../../test/LocationProbe'
import { renderWithProviders } from '../../test/renderWithProviders'
import { CataloguePage } from './CataloguePage'

afterEach(() => vi.unstubAllGlobals())

function BackButton() {
  const navigate = useNavigate()
  return (
    <button type="button" onClick={() => void navigate(-1)}>
      browser back
    </button>
  )
}

const renderCatalogue = (route = '/perfumes') =>
  renderWithProviders(
    <>
      <CataloguePage />
      <LocationProbe />
      <BackButton />
    </>,
    { route },
  )

const location = () => screen.getByTestId('location').textContent ?? ''
const products = (ids: number[], overrides = {}) => makePage(ids.map((id) => makeProduct({ id })), overrides)

const standardApi = (productsHandler?: (url: URL) => Response | object) =>
  mockApi({
    '/api/v1/products': (url) => (productsHandler ? productsHandler(url) : products([1, 2, 3])),
    '/api/v1/catalog/filters': () => makeFilters(),
  })

describe('CataloguePage: URL as the single source of state (AC-S2)', () => {
  it('restores the whole state from the URL into the request and the controls', async () => {
    const api = standardApi()

    renderCatalogue(
      '/perfumes?q=sauv&brand=dior&brand=chanel&category=men&concentration=EDP&volume=50&minPrice=1000000&maxPrice=5000000&sort=price_asc&page=2&pageSize=12',
    )

    await screen.findAllByRole('heading', { level: 3 })
    const query = new URL(api.forPath('/api/v1/products')[0]!.url).searchParams
    expect(query.get('q')).toBe('sauv')
    expect(query.getAll('brand')).toEqual(['dior', 'chanel'])
    expect(query.getAll('category')).toEqual(['men'])
    expect(query.getAll('concentration')).toEqual(['EDP'])
    expect(query.getAll('volume')).toEqual(['50'])
    expect(query.get('minPrice')).toBe('1000000')
    expect(query.get('maxPrice')).toBe('5000000')
    expect(query.get('sort')).toBe('price_asc')
    expect(query.get('page')).toBe('2')
    expect(query.get('pageSize')).toBe('12')

    expect(screen.getByLabelText('Search')).toHaveValue('sauv')
    expect(screen.getByLabelText('Minimum price (₫)')).toHaveValue('1000000')
    expect(screen.getByLabelText('Maximum price (₫)')).toHaveValue('5000000')
    expect(screen.getByLabelText('Dior')).toBeChecked()
    expect(screen.getByLabelText('Chanel')).toBeChecked()
    expect(screen.getByLabelText('Men')).toBeChecked()
    expect(screen.getByLabelText('Eau de Parfum')).toBeChecked()
    expect(screen.getByLabelText('50 ml')).toBeChecked()
    expect(screen.getByLabelText('100 ml')).not.toBeChecked()
    expect(screen.getByLabelText('Sort by')).toHaveValue('price_asc')
    expect(screen.getByLabelText('Per page')).toHaveValue('12')
  })

  it('writes a filter change to the URL, resets the page and sends the new request', async () => {
    const api = standardApi()
    renderCatalogue('/perfumes?page=3')
    await screen.findAllByRole('heading', { level: 3 })

    await userEvent.click(screen.getByLabelText('Dior'))

    expect(location()).toBe('/perfumes?brand=dior')
    await waitFor(() => expect(api.queries('/api/v1/products').at(-1)).toBe('?brand=dior'))
  })

  it('writes sort and page size to the URL and omits the defaults', async () => {
    standardApi()
    renderCatalogue()
    await screen.findAllByRole('heading', { level: 3 })

    await userEvent.selectOptions(screen.getByLabelText('Sort by'), 'price_desc')
    expect(location()).toBe('/perfumes?sort=price_desc')

    await userEvent.selectOptions(screen.getByLabelText('Per page'), '48')
    expect(location()).toBe('/perfumes?sort=price_desc&pageSize=48')

    await userEvent.selectOptions(screen.getByLabelText('Sort by'), 'newest')
    await userEvent.selectOptions(screen.getByLabelText('Per page'), '24')
    expect(location()).toBe('/perfumes')
  })

  it('restores the previous state with the browser back button', async () => {
    standardApi()
    renderCatalogue()
    await screen.findAllByRole('heading', { level: 3 })

    await userEvent.click(screen.getByLabelText('Dior'))
    expect(screen.getByLabelText('Dior')).toBeChecked()

    await userEvent.click(screen.getByRole('button', { name: 'browser back' }))

    expect(location()).toBe('/perfumes')
    expect(screen.getByLabelText('Dior')).not.toBeChecked()
  })

  it('keeps a volume written with a decimal point and matches it numerically', async () => {
    standardApi()
    renderCatalogue('/perfumes?volume=50.0')
    await screen.findAllByRole('heading', { level: 3 })

    expect(screen.getByLabelText('50 ml')).toBeChecked()
    await userEvent.click(screen.getByLabelText('50 ml'))
    expect(location()).toBe('/perfumes')
  })

  it('shows and can remove filter values the API no longer lists', async () => {
    standardApi()
    renderCatalogue('/perfumes?brand=old-brand')
    await screen.findAllByRole('heading', { level: 3 })

    expect(screen.getByLabelText('old-brand')).toBeChecked()
    await userEvent.click(screen.getByRole('button', { name: /Brand: old-brand/ }))
    expect(location()).toBe('/perfumes')
  })

  it('sets the canonical link without the sort and a page-aware title', async () => {
    standardApi()
    renderCatalogue('/perfumes?brand=dior&sort=price_asc&page=2')
    await screen.findAllByRole('heading', { level: 3 })

    expect(document.head.querySelector('link[rel="canonical"]')).toHaveAttribute(
      'href',
      `${window.location.origin}/perfumes?brand=dior&page=2`,
    )
    expect(document.title).toBe('Perfumes – page 2 | UTperfume')
  })
})

describe('CataloguePage: search and price inputs', () => {
  it('applies a search of 2 to 100 characters to the URL', async () => {
    standardApi()
    renderCatalogue()
    await screen.findAllByRole('heading', { level: 3 })

    await userEvent.type(screen.getByLabelText('Search'), 'sauvage')
    await userEvent.click(screen.getByRole('button', { name: 'Apply' }))

    expect(location()).toBe('/perfumes?q=sauvage')
  })

  it.each([
    ['a', 'Enter at least 2 characters.'],
    ['x'.repeat(101), 'Enter at most 100 characters.'],
  ])('rejects the search text %j next to the field without calling the API', async (text, message) => {
    const api = standardApi()
    renderCatalogue()
    await screen.findAllByRole('heading', { level: 3 })
    const before = api.forPath('/api/v1/products').length

    await userEvent.type(screen.getByLabelText('Search'), text)
    await userEvent.click(screen.getByRole('button', { name: 'Apply' }))

    const input = screen.getByLabelText('Search')
    expect(input).toHaveAccessibleDescription(message)
    expect(input).toHaveAttribute('aria-invalid', 'true')
    expect(location()).toBe('/perfumes')
    expect(api.forPath('/api/v1/products')).toHaveLength(before)
  })

  it.each(['1500000.5', '1.500.000', '-5', 'abc'])('rejects the decimal or non-numeric price %j', async (text) => {
    const api = standardApi()
    renderCatalogue()
    await screen.findAllByRole('heading', { level: 3 })
    const before = api.forPath('/api/v1/products').length

    await userEvent.type(screen.getByLabelText('Minimum price (₫)'), text)
    await userEvent.click(screen.getByRole('button', { name: 'Apply' }))

    expect(screen.getByLabelText('Minimum price (₫)')).toHaveAccessibleDescription(/whole dong/)
    expect(location()).toBe('/perfumes')
    expect(api.forPath('/api/v1/products')).toHaveLength(before)
  })

  it('rejects a maximum price with a decimal next to the maximum field', async () => {
    standardApi()
    renderCatalogue()
    await screen.findAllByRole('heading', { level: 3 })

    await userEvent.type(screen.getByLabelText('Maximum price (₫)'), '99.5')
    await userEvent.click(screen.getByRole('button', { name: 'Apply' }))

    expect(screen.getByLabelText('Maximum price (₫)')).toHaveAccessibleDescription(/whole dong/)
  })

  it('rejects a minimum above the maximum', async () => {
    standardApi()
    renderCatalogue()
    await screen.findAllByRole('heading', { level: 3 })

    await userEvent.type(screen.getByLabelText('Minimum price (₫)'), '5000000')
    await userEvent.type(screen.getByLabelText('Maximum price (₫)'), '1000000')
    await userEvent.click(screen.getByRole('button', { name: 'Apply' }))

    expect(screen.getByLabelText('Minimum price (₫)')).toHaveAccessibleDescription(/must not be above/)
    expect(location()).toBe('/perfumes')
  })

  it('applies valid whole-dong prices, including with Enter', async () => {
    standardApi()
    renderCatalogue()
    await screen.findAllByRole('heading', { level: 3 })

    await userEvent.type(screen.getByLabelText('Minimum price (₫)'), '1000000')
    await userEvent.type(screen.getByLabelText('Maximum price (₫)'), '5000000{Enter}')

    expect(location()).toBe('/perfumes?minPrice=1000000&maxPrice=5000000')
  })

  it('uses the price range from the filters as a hint', async () => {
    standardApi()
    renderCatalogue()
    await screen.findAllByRole('heading', { level: 3 })

    await waitFor(() => expect(screen.getByLabelText('Minimum price (₫)')).toHaveAttribute('placeholder', expect.stringContaining('1.250.000')))
  })
})

describe('CataloguePage: 400 field errors from the API', () => {
  it('shows the API message next to the price field it is keyed to', async () => {
    standardApi(() =>
      problemResponse(400, {
        title: 'One or more validation errors occurred.',
        errors: { minPrice: ['minPrice must not be greater than maxPrice.'] },
      }),
    )

    renderCatalogue('/perfumes?minPrice=100&maxPrice=50')

    const input = await screen.findByLabelText('Minimum price (₫)')
    await waitFor(() => expect(input).toHaveAccessibleDescription('minPrice must not be greater than maxPrice.'))
    expect(input).toHaveAttribute('aria-invalid', 'true')
    expect(screen.getByLabelText('Maximum price (₫)')).not.toHaveAttribute('aria-invalid')
  })

  it('maps a q error to the search field and a sort error to the sort control area', async () => {
    standardApi(() =>
      problemResponse(400, { errors: { q: ['q must be between 2 and 100 characters.'], sort: ['sort must be one of: newest.'] } }),
    )

    renderCatalogue('/perfumes?q=a&sort=cheapest')

    await waitFor(() =>
      expect(screen.getByLabelText('Search')).toHaveAccessibleDescription('q must be between 2 and 100 characters.'),
    )
    expect(await screen.findByText('sort must be one of: newest.')).toBeInTheDocument()
  })

  it('maps a page error and passes the raw URL value to the API so it can answer', async () => {
    const api = standardApi(() => problemResponse(400, { errors: { page: ['page must be a whole number.'] } }))

    renderCatalogue('/perfumes?page=abc')

    expect(await screen.findByText('page must be a whole number.')).toBeInTheDocument()
    expect(new URL(api.forPath('/api/v1/products')[0]!.url).searchParams.get('page')).toBe('abc')
  })

  it('maps a filter-group error to its group', async () => {
    standardApi(() => problemResponse(400, { errors: { brand: ['brand must be a lower-case slug.'] } }))

    renderCatalogue('/perfumes?brand=BAD_SLUG')

    const group = await screen.findByRole('group', { name: 'Brand' })
    await waitFor(() => expect(group).toHaveAccessibleDescription('brand must be a lower-case slug.'))
  })

  it('shows a general message for a 400 and no products', async () => {
    standardApi(() => problemResponse(400, { errors: { minPrice: ['bad'] } }))

    renderCatalogue('/perfumes?minPrice=x')

    expect(await screen.findByText(/search options are not valid/)).toBeInTheDocument()
    expect(screen.queryAllByRole('heading', { level: 3 })).toHaveLength(0)
  })
})

describe('CataloguePage: results, states and pagination', () => {
  it('shows the total and the product cards', async () => {
    standardApi(() => products([1, 2, 3], { totalItems: 50, pageSize: 24, totalPages: 3 }))
    renderCatalogue()

    expect(await screen.findByText('50 perfumes')).toBeInTheDocument()
    expect(within(screen.getByRole('list', { name: 'Perfumes' })).getAllByRole('listitem')).toHaveLength(3)
  })

  it('uses the singular for one perfume', async () => {
    standardApi(() => products([1]))
    renderCatalogue()

    expect(await screen.findByText('1 perfume')).toBeInTheDocument()
  })

  it('shows an empty state with a way to clear the filters', async () => {
    standardApi(() => makePage([], { totalItems: 0 }))
    renderCatalogue('/perfumes?brand=dior')

    expect(await screen.findByText('No perfumes match your search.')).toBeInTheDocument()
    const clearButtons = screen.getAllByRole('button', { name: 'Clear all filters' })
    await userEvent.click(clearButtons[clearButtons.length - 1]!)

    expect(location()).toBe('/perfumes')
  })

  it('explains a page past the end and links to the first page', async () => {
    standardApi(() => makePage([], { totalItems: 50, page: 10, totalPages: 3 }))
    renderCatalogue('/perfumes?page=10')

    expect(await screen.findByText(/no results on this page/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Go to the first page' })).toBeInTheDocument()
  })

  it('shows a loading state first', () => {
    mockApi({
      '/api/v1/products': () => new Promise<never>(() => undefined),
      '/api/v1/catalog/filters': () => makeFilters(),
    })
    renderCatalogue()

    expect(screen.getByText('Loading perfumes')).toBeInTheDocument()
  })

  it('shows an error with a retry for a server failure', async () => {
    let calls = 0
    standardApi(() => (++calls === 1 ? problemResponse(500) : products([7])))
    renderCatalogue()

    expect(await screen.findByText('The perfumes could not be loaded.')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }))

    expect(await screen.findByRole('heading', { level: 3, name: 'Product 7' })).toBeInTheDocument()
  })

  it('keeps searching usable when the filter options fail to load', async () => {
    mockApi({
      '/api/v1/products': () => products([1]),
      '/api/v1/catalog/filters': () => problemResponse(500),
    })
    renderCatalogue()

    expect(await screen.findByText(/filter options could not be loaded/)).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 3, name: 'Product 1' })).toBeInTheDocument()
  })

  it('changes the page in the URL from the pagination links', async () => {
    standardApi(() => products([1, 2], { totalItems: 50, pageSize: 24, totalPages: 3 }))
    renderCatalogue('/perfumes?brand=dior')
    await screen.findAllByRole('heading', { level: 3 })

    await userEvent.click(screen.getByRole('link', { name: 'Page 2' }))

    expect(location()).toBe('/perfumes?brand=dior&page=2')
  })

  it('requests the new page after the URL changes', async () => {
    const api = standardApi((url) => products([1, 2], { totalItems: 50, pageSize: 24, totalPages: 3, page: Number(url.searchParams.get('page') ?? 1) }))
    renderCatalogue()
    await screen.findAllByRole('heading', { level: 3 })

    await userEvent.click(screen.getByRole('link', { name: 'Next' }))

    await waitFor(() => expect(api.queries('/api/v1/products').at(-1)).toBe('?page=2'))
  })

  it('removes an active filter with its chip and returns to page 1', async () => {
    standardApi()
    renderCatalogue('/perfumes?brand=dior&minPrice=1000000&page=2')
    await screen.findAllByRole('heading', { level: 3 })

    await userEvent.click(screen.getByRole('button', { name: /From 1\.000\.000/ }))

    expect(location()).toBe('/perfumes?brand=dior')
  })
})

describe('CataloguePage: requests', () => {
  it('keeps the previous results on screen while the next ones load (keepPreviousData)', async () => {
    let release: (value: Response) => void = () => undefined
    standardApi((url) =>
      url.searchParams.get('brand') === 'dior'
        ? (new Promise<Response>((resolve) => (release = resolve)) as unknown as Response)
        : products([1, 2]),
    )
    renderCatalogue()
    await screen.findAllByRole('heading', { level: 3 })

    await userEvent.click(screen.getByLabelText('Dior'))

    await waitFor(() => expect(document.querySelector('[aria-busy="true"]')).not.toBeNull())
    expect(screen.getAllByRole('heading', { level: 3 })).toHaveLength(2)
    expect(screen.queryByText('Loading perfumes')).not.toBeInTheDocument()

    release(jsonResponse(products([9])))
    expect(await screen.findByRole('heading', { level: 3, name: 'Product 9' })).toBeInTheDocument()
    expect(document.querySelector('[aria-busy="true"]')).toBeNull()
  })

  it('cancels the request a newer filter has made obsolete (query signal)', async () => {
    const api = standardApi((url) =>
      url.searchParams.has('sort') ? (new Promise<Response>(() => undefined) as unknown as Response) : products([1]),
    )
    renderCatalogue('/perfumes?sort=price_asc')

    await waitFor(() => expect(api.forPath('/api/v1/products')).toHaveLength(1))
    const first = api.forPath('/api/v1/products')[0]!
    expect(first.signal.aborted).toBe(false)

    await userEvent.selectOptions(screen.getByLabelText('Sort by'), 'name_asc')

    await waitFor(() => expect(first.signal.aborted).toBe(true))
  })

  it('goes through the typed client only (no other endpoint is called)', async () => {
    const api = standardApi()
    renderCatalogue()
    await screen.findAllByRole('heading', { level: 3 })

    expect(new Set(api.requests.map((r) => new URL(r.url).pathname))).toEqual(
      new Set(['/api/v1/products', '/api/v1/catalog/filters']),
    )
  })
})

describe('CataloguePage: accessibility and content rules', () => {
  it('labels every control, with the filter groups as fieldsets', async () => {
    standardApi()
    renderCatalogue()
    await screen.findAllByRole('heading', { level: 3 })

    expect(screen.getByRole('search', { name: 'Search and price' })).toBeInTheDocument()
    for (const name of ['Brand', 'Category', 'Concentration', 'Volume']) {
      expect(await screen.findByRole('group', { name })).toBeInTheDocument()
    }
    expect(screen.getByLabelText('Search')).toBeInTheDocument()
    expect(screen.getByLabelText('Sort by')).toBeInTheDocument()
    expect(screen.getByLabelText('Per page')).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 1, name: 'Perfumes' })).toBeInTheDocument()
  })

  it('toggles a filter with the keyboard', async () => {
    standardApi()
    renderCatalogue()
    await screen.findAllByRole('heading', { level: 3 })

    const checkbox = await screen.findByLabelText('Dior')
    checkbox.focus()
    await userEvent.keyboard(' ')

    expect(location()).toBe('/perfumes?brand=dior')
  })

  it('never renders a quantity anywhere on the page', async () => {
    standardApi(() => products([1, 2, 3], { totalItems: 3 }))
    const { container } = renderCatalogue()
    await screen.findAllByRole('heading', { level: 3 })

    expect(container.textContent?.toLowerCase()).not.toContain('quantity')
    expect(container.textContent).not.toMatch(/\b\d+ (left|remaining)\b/i)
  })
})
