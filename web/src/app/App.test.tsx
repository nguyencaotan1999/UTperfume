import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { mockApi } from '../test/fetchMock'
import { makeFilters, makeHome, makePage } from '../test/fixtures'
import { renderWithProviders } from '../test/renderWithProviders'
import { App } from './App'
import { ErrorBoundary } from './ErrorBoundary'

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const api = () =>
  mockApi({
    '/api/v1/home': () => makeHome(),
    '/api/v1/products': () => makePage([]),
    '/api/v1/catalog/filters': () => makeFilters(),
  })

describe('App shell', () => {
  it('renders the layout with skip link, navigation and footer around a page', async () => {
    api()
    renderWithProviders(<App />, { route: '/' })

    expect(screen.getByRole('link', { name: 'Skip to content' })).toHaveAttribute('href', '#main')
    expect(screen.getByRole('navigation', { name: 'Main' })).toBeInTheDocument()
    expect(screen.getByRole('contentinfo')).toBeInTheDocument()
    expect(await screen.findByRole('heading', { level: 1, name: 'Find your signature fragrance' })).toBeInTheDocument()
  })

  it('shows the not found page for an unknown client route (AC-S4)', () => {
    renderWithProviders(<App />, { route: '/some/unknown/route' })

    expect(screen.getByRole('heading', { level: 1, name: 'Page not found' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Back to the home page' })).toHaveAttribute('href', '/')
    expect(document.head.querySelector('meta[name="robots"]')).toHaveAttribute('content', 'noindex')
  })

  it('routes to the catalogue', async () => {
    api()
    renderWithProviders(<App />, { route: '/perfumes' })

    expect(await screen.findByRole('heading', { level: 1, name: 'Perfumes' })).toBeInTheDocument()
  })

  it('navigates with the main links', async () => {
    api()
    renderWithProviders(<App />, { route: '/' })

    await userEvent.click(screen.getByRole('link', { name: 'Perfumes' }))

    expect(await screen.findByRole('heading', { level: 1, name: 'Perfumes' })).toBeInTheDocument()
  })
})

describe('ErrorBoundary', () => {
  function Bomb(): never {
    throw new Error('boom secret detail')
  }

  it('shows a generic message without leaking the error text', () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined)

    render(
      <MemoryRouter>
        <ErrorBoundary>
          <Bomb />
        </ErrorBoundary>
      </MemoryRouter>,
    )

    const alert = screen.getByRole('alert')
    expect(alert).toHaveTextContent('Something went wrong')
    expect(alert.textContent).not.toContain('boom secret detail')
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Home' })).toBeInTheDocument()
  })

  it('renders its children when nothing fails', () => {
    render(
      <ErrorBoundary>
        <p>Fine</p>
      </ErrorBoundary>,
    )

    expect(screen.getByText('Fine')).toBeInTheDocument()
  })
})
