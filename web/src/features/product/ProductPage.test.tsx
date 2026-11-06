import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Route, Routes } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { NotFoundPage } from '../../app/NotFoundPage'
import { jsonResponse, mockApi, problemResponse } from '../../test/fetchMock'
import { makeDetail, makeProduct, makeVariant } from '../../test/fixtures'
import { renderWithProviders } from '../../test/renderWithProviders'
import { plain } from '../../test/text'
import { ProductPage } from './ProductPage'

afterEach(() => vi.unstubAllGlobals())

const renderProduct = (route = '/perfumes/sauvage') =>
  renderWithProviders(
    <Routes>
      <Route path="perfumes/:slug" element={<ProductPage />} />
      <Route path="*" element={<NotFoundPage />} />
    </Routes>,
    { route },
  )

const api = (detail = makeDetail(), related: object[] = []) =>
  mockApi({
    '/api/v1/products/sauvage': () => detail,
    '/api/v1/products/sauvage/related': () => related,
  })

const jsonLd = () => {
  const script = document.querySelector('script[type="application/ld+json"]')
  return JSON.parse(script?.textContent ?? 'null') as Record<string, unknown>
}

describe('ProductPage: content (AC-P1)', () => {
  it('shows the name, brand, concentration, short description, categories and description', async () => {
    api()
    renderProduct()

    expect(await screen.findByRole('heading', { level: 1, name: 'Sauvage' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Dior' })).toHaveAttribute('href', '/brands/dior')
    expect(screen.getByText('Eau de Parfum')).toBeInTheDocument()
    expect(screen.getByText('Fresh and woody', { selector: 'p.product__short' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Men' })).toHaveAttribute('href', '/categories/men')
    expect(screen.getByText('First paragraph.')).toBeInTheDocument()
    expect(screen.getByText('Second paragraph.')).toBeInTheDocument()
  })

  it('renders the description as plain text, never as HTML', async () => {
    api(makeDetail({ description: '<img src=x onerror=alert(1)>' }))
    const { container } = renderProduct()

    await screen.findByRole('heading', { level: 1, name: 'Sauvage' })

    expect(container.querySelector('img[src="x"]')).toBeNull()
    expect(screen.getByText('<img src=x onerror=alert(1)>')).toBeInTheDocument()
  })

  it('shows the images with their API alt text and switches the large image from the thumbnails', async () => {
    api()
    renderProduct()

    const main = await screen.findByRole('img', { name: 'Front of the bottle' })
    expect(main).toHaveAttribute('src', 'https://images.test/a.png')

    await userEvent.click(screen.getByRole('button', { name: 'Side of the bottle' }))

    expect(screen.getByRole('img', { name: 'Side of the bottle' })).toHaveAttribute('src', 'https://images.test/b.png')
    expect(screen.getByRole('button', { name: 'Side of the bottle' })).toHaveAttribute('aria-pressed', 'true')
  })

  it('shows a labelled placeholder when there are no images', async () => {
    api(makeDetail({ images: [] }))
    renderProduct()

    expect(await screen.findByRole('img', { name: 'Sauvage, no image available' })).toBeInTheDocument()
  })

  it('shows a single image without thumbnails', async () => {
    api(makeDetail({ images: [{ url: 'https://images.test/a.png', altText: 'Only one', isPrimary: true, width: null, height: null }] }))
    renderProduct()

    expect(await screen.findByRole('img', { name: 'Only one' })).toBeInTheDocument()
    expect(screen.queryByRole('list', { name: 'Product images' })).not.toBeInTheDocument()
  })
})

describe('ProductPage: variants, price and availability (AC-P2, AC-P3, BR-08)', () => {
  const variants = [
    makeVariant({ sku: 'V-30', volumeMl: 30, price: 1_000_000, availability: 'OutOfStock' }),
    makeVariant({ sku: 'V-50', volumeMl: 50, price: 2_000_000, availability: 'LowStock' }),
    makeVariant({ sku: 'V-100', volumeMl: 100, price: 3_200_000, originalPrice: 4_000_000, discountPercent: 20, availability: 'InStock' }),
  ]

  it('lists every variant with volume, price and its status, as a labelled radio group', async () => {
    api(makeDetail({ variants }))
    renderProduct()

    const group = await screen.findByRole('group', { name: 'Size' })
    const radios = within(group).getAllByRole('radio')
    expect(radios).toHaveLength(3)
    expect(plain((within(group).getByRole('radio', { name: /30 ml/ }) as HTMLInputElement).labels?.[0]?.textContent)).toContain('1.000.000 ₫')
    expect(within(group).getByRole('radio', { name: /30 ml/ })).toHaveAccessibleName(/Out of stock/)
    expect(within(group).getByRole('radio', { name: /50 ml/ })).toHaveAccessibleName(/Low stock/)
    expect(within(group).getByRole('radio', { name: /100 ml/ })).toHaveAccessibleName(/In stock/)
  })

  it('starts on the first variant that can be bought', async () => {
    api(makeDetail({ variants }))
    renderProduct()

    expect(await screen.findByRole('radio', { name: /50 ml/ })).toBeChecked()
  })

  it('starts on the first variant when none can be bought', async () => {
    api(makeDetail({ variants: [makeVariant({ sku: 'A', volumeMl: 30, availability: 'OutOfStock' }), makeVariant({ sku: 'B', volumeMl: 50, availability: 'OutOfStock' })] }))
    renderProduct()

    expect(await screen.findByRole('radio', { name: /30 ml/ })).toBeChecked()
  })

  it('updates the price, compare-at price, discount and status when another variant is selected', async () => {
    api(makeDetail({ variants }))
    renderProduct()
    await screen.findByRole('group', { name: 'Size' })
    const summary = () => document.querySelector('.variants__selected') as HTMLElement

    expect(plain(summary().textContent)).toContain('2.000.000 ₫')
    expect(summary().querySelector('s')).toBeNull()
    expect(within(summary()).getByText('Low stock')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('radio', { name: /100 ml/ }))

    expect(plain(summary().textContent)).toContain('3.200.000 ₫')
    expect(plain(summary().querySelector('s')?.textContent)).toContain('4.000.000 ₫')
    expect(summary()).toHaveTextContent('Save 20%')
    expect(within(summary()).getByText('In stock')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('radio', { name: /30 ml/ }))

    expect(within(summary()).getByText('Out of stock')).toBeInTheDocument()
    expect(summary().querySelector('s')).toBeNull()
  })

  it('strikes the compare-at price through only when it is greater than the price', async () => {
    api(makeDetail({ variants: [makeVariant({ price: 3_000_000, originalPrice: 3_000_000, discountPercent: null })] }))
    renderProduct()
    await screen.findByRole('group', { name: 'Size' })

    expect(document.querySelector('.variants__selected s')).toBeNull()
    expect(document.body.textContent).not.toContain('Save')
  })

  it('selects a size with the keyboard (arrow keys)', async () => {
    api(makeDetail({ variants }))
    renderProduct()
    const checked = await screen.findByRole('radio', { name: /50 ml/ })
    checked.focus()

    await userEvent.keyboard('{ArrowDown}')

    expect(screen.getByRole('radio', { name: /100 ml/ })).toBeChecked()
  })

  it('shows all three availability labels and never a quantity or a digit-based stock text', async () => {
    api(makeDetail({ variants }))
    const { container } = renderProduct()
    await screen.findByRole('group', { name: 'Size' })

    for (const label of ['Out of stock', 'Low stock', 'In stock']) {
      expect(screen.getAllByText(label).length).toBeGreaterThan(0)
    }
    expect(container.textContent?.toLowerCase()).not.toContain('quantity')
    expect(container.textContent).not.toMatch(/\b\d+ (left|remaining|available|in stock)\b/i)
  })

  it('says so when a perfume has no sizes', async () => {
    api(makeDetail({ variants: [] }))
    renderProduct()

    expect(await screen.findByText(/No sizes are available/)).toBeInTheDocument()
  })
})

describe('ProductPage: notes pyramid', () => {
  it('lists top, heart and base notes in that order', async () => {
    api()
    renderProduct()

    const section = await screen.findByRole('region', { name: 'Fragrance notes' })
    const tiers = within(section).getAllByRole('heading', { level: 3 }).map((h) => h.textContent)
    expect(tiers).toEqual(['Top notes', 'Heart notes', 'Base notes'])
    expect(within(section).getByText('Bergamot')).toBeInTheDocument()
    expect(within(section).getByText('Pepper')).toBeInTheDocument()
    expect(within(section).getByText('Amber')).toBeInTheDocument()
  })

  it('leaves out an empty tier, and the whole section when there are no notes', async () => {
    api(makeDetail({ notes: { top: [{ slug: 'lemon', name: 'Lemon' }], heart: [], base: [] } }))
    const first = renderProduct()
    const section = await screen.findByRole('region', { name: 'Fragrance notes' })
    expect(within(section).getAllByRole('heading', { level: 3 }).map((h) => h.textContent)).toEqual(['Top notes'])
    first.unmount()

    api(makeDetail({ notes: { top: [], heart: [], base: [] } }))
    renderProduct()
    await screen.findByRole('heading', { level: 1, name: 'Sauvage' })
    expect(screen.queryByRole('region', { name: 'Fragrance notes' })).not.toBeInTheDocument()
  })
})

describe('ProductPage: SEO (AC-S1)', () => {
  it('sets the title "{name} – {brand} | UTperfume" with an en dash, the description and the canonical link', async () => {
    api()
    renderProduct()
    await screen.findByRole('heading', { level: 1, name: 'Sauvage' })

    await waitFor(() => expect(document.title).toBe('Sauvage – Dior | UTperfume'))
    expect(document.title).toContain('–')
    expect(document.head.querySelector('meta[name="description"]')).toHaveAttribute('content', 'Fresh and woody')
    expect(document.head.querySelector('link[rel="canonical"]')).toHaveAttribute('href', `${window.location.origin}/perfumes/sauvage`)
  })

  it('uses the short description when the API sends no meta description', async () => {
    api(makeDetail({ seo: { metaTitle: 'Sauvage – Dior | UTperfume', metaDescription: null } }))
    renderProduct()
    await screen.findByRole('heading', { level: 1, name: 'Sauvage' })

    expect(document.head.querySelector('meta[name="description"]')).toHaveAttribute('content', 'Fresh and woody')
  })

  it('emits Product JSON-LD with VND offers, integer prices and availability, and no quantity', async () => {
    api(
      makeDetail({
        variants: [
          makeVariant({ sku: 'V-50', volumeMl: 50, price: 2_000_000, availability: 'LowStock' }),
          makeVariant({ sku: 'V-100', volumeMl: 100, price: 3_200_000, availability: 'InStock' }),
        ],
      }),
    )
    renderProduct()
    await screen.findByRole('heading', { level: 1, name: 'Sauvage' })

    const data = jsonLd() as {
      '@type': string
      name: string
      brand: { name: string }
      offers: { priceCurrency: string; lowPrice: number; highPrice: number; offerCount: number; availability: string; offers: Array<Record<string, unknown>> }
    }
    expect(data['@type']).toBe('Product')
    expect(data.name).toBe('Sauvage')
    expect(data.brand.name).toBe('Dior')
    expect(data.offers.priceCurrency).toBe('VND')
    expect(data.offers.lowPrice).toBe(2_000_000)
    expect(data.offers.highPrice).toBe(3_200_000)
    expect(data.offers.offerCount).toBe(2)
    expect(data.offers.availability).toBe('https://schema.org/InStock')
    expect(data.offers.offers.map((o) => o.availability)).toEqual(['https://schema.org/LimitedAvailability', 'https://schema.org/InStock'])
    for (const offer of data.offers.offers) {
      expect(offer.priceCurrency).toBe('VND')
      expect(Number.isInteger(offer.price)).toBe(true)
    }
    expect(JSON.stringify(data).toLowerCase()).not.toContain('quantity')
  })

  it('reports out of stock in JSON-LD when every variant is out of stock', async () => {
    api(makeDetail({ variants: [makeVariant({ availability: 'OutOfStock' })] }))
    renderProduct()
    await screen.findByRole('heading', { level: 1, name: 'Sauvage' })

    expect((jsonLd() as { offers: { availability: string } }).offers.availability).toBe('https://schema.org/OutOfStock')
  })

  it('escapes "<" in JSON-LD so a product name cannot close the script', async () => {
    api(makeDetail({ name: 'Evil </script><b>x', seo: { metaTitle: 'Evil | UTperfume', metaDescription: null } }))
    renderProduct()
    await screen.findByRole('heading', { level: 1 })

    const script = document.querySelector('script[type="application/ld+json"]')
    expect(script?.textContent).not.toContain('<')
    expect(JSON.parse(script?.textContent ?? '{}').name).toBe('Evil </script><b>x')
  })
})

describe('ProductPage: not found and errors (AC-P4, AC-S4)', () => {
  it('shows the not found page for an unknown or hidden slug, not a crash', async () => {
    mockApi({ '/api/v1/products/missing': () => problemResponse(404, { title: 'Not Found' }) })
    renderProduct('/perfumes/missing')

    expect(await screen.findByRole('heading', { level: 1, name: 'Page not found' })).toBeInTheDocument()
    expect(document.head.querySelector('meta[name="robots"]')).toHaveAttribute('content', 'noindex')
  })

  it('shows an error with a retry for a server failure', async () => {
    let calls = 0
    mockApi({
      '/api/v1/products/sauvage': () => (++calls === 1 ? problemResponse(500) : jsonResponse(makeDetail())),
      '/api/v1/products/sauvage/related': () => [],
    })
    renderProduct()

    expect(await screen.findByRole('alert')).toHaveTextContent('could not be loaded')
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }))

    expect(await screen.findByRole('heading', { level: 1, name: 'Sauvage' })).toBeInTheDocument()
  })

  it('shows a loading state first', () => {
    mockApi({ '/api/v1/products/sauvage': () => new Promise<never>(() => undefined) })
    renderProduct()

    expect(screen.getByText('Loading the perfume')).toBeInTheDocument()
  })
})

describe('ProductPage: related products (E4, AC-R1)', () => {
  it('shows the related products from the E4 hook as cards', async () => {
    const mock = api(makeDetail(), [makeProduct({ id: 2, name: 'Related One', slug: 'related-one' }), makeProduct({ id: 3, name: 'Related Two' })])
    renderProduct()

    const section = await screen.findByRole('region', { name: 'You may also like' })
    expect(within(section).getByRole('heading', { level: 3, name: 'Related One' })).toBeInTheDocument()
    expect(within(section).getByRole('link', { name: /Related One/ })).toHaveAttribute('href', '/perfumes/related-one')
    expect(within(section).getAllByRole('article')).toHaveLength(2)
    expect(mock.forPath('/api/v1/products/sauvage/related')).toHaveLength(1)
  })

  it('leaves the section out when there are no related products', async () => {
    api(makeDetail(), [])
    renderProduct()
    await screen.findByRole('heading', { level: 1, name: 'Sauvage' })

    await waitFor(() => expect(screen.queryByRole('region', { name: 'You may also like' })).not.toBeInTheDocument())
  })

  it('still shows the product when the related call fails', async () => {
    mockApi({
      '/api/v1/products/sauvage': () => makeDetail(),
      '/api/v1/products/sauvage/related': () => problemResponse(500),
    })
    renderProduct()

    expect(await screen.findByRole('heading', { level: 1, name: 'Sauvage' })).toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'You may also like' })).not.toBeInTheDocument()
  })

  it('never shows a quantity in the related cards', async () => {
    api(makeDetail(), [makeProduct({ id: 2, inStock: false })])
    const { container } = renderProduct()
    await screen.findByRole('region', { name: 'You may also like' })

    expect(container.textContent?.toLowerCase()).not.toContain('quantity')
  })
})
