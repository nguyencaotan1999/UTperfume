import { screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { renderWithProviders } from '../test/renderWithProviders'
import { App } from './App'

afterEach(() => vi.unstubAllGlobals())

describe('App', () => {
  it('renders the heading and the service status', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response('{"status":"ok"}', { status: 200, headers: { 'content-type': 'application/json' } })),
    )

    renderWithProviders(<App />)

    expect(screen.getByRole('heading', { name: 'UTperfume' })).toBeInTheDocument()
    expect(await screen.findByText('Service status: ok')).toBeInTheDocument()
  })

  it('shows an error state when the service fails', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response('{}', { status: 400, headers: { 'content-type': 'application/json' } })))

    renderWithProviders(<App />)

    expect(await screen.findByRole('alert')).toHaveTextContent('unavailable')
  })
})
