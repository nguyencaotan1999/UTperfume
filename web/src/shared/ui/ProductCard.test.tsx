import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import type { ProductSummary } from '../../api/types'
import { makeProduct } from '../../test/fixtures'
import { plain } from '../../test/text'
import { ProductCard } from './ProductCard'

const renderCard = (overrides: Partial<ProductSummary> = {}) =>
  render(
    <MemoryRouter>
      <ProductCard product={makeProduct(overrides)} />
    </MemoryRouter>,
  )


describe('ProductCard', () => {
  it('links to the product page and shows name, brand, concentration and volumes', () => {
    renderCard({ slug: 'dior-sauvage-edp', name: 'Sauvage' })

    expect(screen.getByRole('link', { name: /Sauvage/ })).toHaveAttribute('href', '/perfumes/dior-sauvage-edp')
    expect(screen.getByRole('heading', { name: 'Sauvage', level: 3 })).toBeInTheDocument()
    expect(screen.getByText('Dior')).toBeInTheDocument()
    expect(screen.getByText(/Eau de Parfum · 50 ml, 100 ml/)).toBeInTheDocument()
  })

  it('shows the from-price in dong with no decimals', () => {
    renderCard({ fromPrice: 1_250_000 })

    expect(plain(document.body.textContent)).toContain('From 1.250.000 ₫')
  })

  it('shows the compare-at price struck through only when it is higher', () => {
    const first = renderCard({ fromPrice: 3_200_000, fromOriginalPrice: 4_000_000 })
    expect(plain(first.container.querySelector('s')?.textContent)).toContain('4.000.000 ₫')
    first.unmount()

    const second = renderCard({ fromPrice: 3_200_000, fromOriginalPrice: null })
    expect(second.container.querySelector('s')).toBeNull()
  })

  it('shows the image with its alt text, or a labelled placeholder when there is none', () => {
    const first = renderCard({ primaryImage: { url: 'https://images.test/a.png', altText: 'Front view' } })
    expect(screen.getByRole('img', { name: 'Front view' })).toHaveAttribute('src', 'https://images.test/a.png')
    first.unmount()

    renderCard({ primaryImage: null, name: 'Bare' })
    expect(screen.getByRole('img', { name: 'Bare, no image available' })).toBeInTheDocument()
  })

  it('falls back to the product name when the image has no alt text', () => {
    renderCard({ name: 'Sauvage', primaryImage: { url: 'https://images.test/a.png', altText: null } })

    expect(screen.getByRole('img', { name: 'Sauvage' })).toBeInTheDocument()
  })

  it('shows the availability status and never a quantity', () => {
    const first = renderCard({ inStock: true })
    expect(screen.getByText('In stock')).toBeInTheDocument()
    first.unmount()

    const out = renderCard({ inStock: false })
    expect(screen.getByText('Out of stock')).toBeInTheDocument()
    expect(out.container.textContent?.toLowerCase()).not.toContain('quantity')
  })

  it('shows the New badge only for new products', () => {
    const first = renderCard({ isNew: true })
    expect(screen.getByText('New')).toBeInTheDocument()
    first.unmount()

    renderCard({ isNew: false })
    expect(screen.queryByText('New')).not.toBeInTheDocument()
  })
})
