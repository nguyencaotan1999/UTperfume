import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { PublicLayout } from './PublicLayout'

afterEach(() => vi.restoreAllMocks())

function Bomb(): never {
  throw new Error('page failed')
}

const renderLayout = () =>
  render(
    <MemoryRouter initialEntries={['/']}>
      <Routes>
        <Route element={<PublicLayout />}>
          <Route index element={<Bomb />} />
          <Route path="perfumes" element={<h1>Catalogue page</h1>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  )

describe('PublicLayout error boundary', () => {
  it('keeps the header and navigation visible when a page throws', () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined)

    renderLayout()

    expect(screen.getByRole('alert')).toHaveTextContent('Something went wrong')
    expect(screen.getByRole('navigation', { name: 'Main' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'UTperfume' })).toBeInTheDocument()
    expect(screen.getByRole('contentinfo')).toBeInTheDocument()
  })

  it('clears the error when the user navigates to another page', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined)
    renderLayout()
    expect(screen.getByRole('alert')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('link', { name: 'Perfumes' }))

    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 1, name: 'Catalogue page' })).toBeInTheDocument()
  })
})
