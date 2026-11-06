import { render } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { Seo } from './Seo'
import { formatTitle, serializeJsonLd } from './seoText'

describe('Seo', () => {
  it('sets the document title with the site name and the meta description', () => {
    render(<Seo title="Perfumes" description="Browse perfumes." />)

    expect(document.title).toBe('Perfumes | UTperfume')
    expect(document.head.querySelector('meta[name="description"]')).toHaveAttribute('content', 'Browse perfumes.')
  })

  it('uses a default title without a page title', () => {
    render(<Seo />)

    expect(document.title).toBe(formatTitle())
    expect(document.title).toContain('UTperfume')
  })

  it('emits an absolute canonical link from a site-relative path', () => {
    render(<Seo title="Perfumes" canonicalPath="/perfumes?brand=dior" />)

    expect(document.head.querySelector('link[rel="canonical"]')).toHaveAttribute(
      'href',
      `${window.location.origin}/perfumes?brand=dior`,
    )
  })

  it('marks a page noindex when asked', () => {
    render(<Seo title="Page not found" noindex />)

    expect(document.head.querySelector('meta[name="robots"]')).toHaveAttribute('content', 'noindex')
  })

  it('renders JSON-LD scripts', () => {
    render(<Seo title="Product" jsonLd={[{ '@context': 'https://schema.org', '@type': 'Product', name: 'Sauvage' }]} />)

    const script = document.querySelector('script[type="application/ld+json"]')
    expect(JSON.parse(script?.textContent ?? '{}')).toMatchObject({ '@type': 'Product', name: 'Sauvage' })
  })
})

describe('serializeJsonLd', () => {
  it('escapes "<" so data cannot close the script element', () => {
    const text = serializeJsonLd({ name: '</script><script>alert(1)</script>' })

    expect(text).not.toContain('<')
    expect(JSON.parse(text)).toEqual({ name: '</script><script>alert(1)</script>' })
  })
})
