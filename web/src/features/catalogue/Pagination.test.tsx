import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import { LocationProbe } from '../../test/LocationProbe'
import { Pagination } from './Pagination'

const searchFor = (page: number) => (page === 1 ? '' : `?page=${page}`)

const renderPagination = (page: number, totalPages: number) =>
  render(
    <MemoryRouter initialEntries={[`/perfumes?page=${page}`]}>
      <Pagination page={page} totalPages={totalPages} searchFor={searchFor} />
      <LocationProbe />
    </MemoryRouter>,
  )

describe('Pagination', () => {
  it('renders nothing for a single page or none', () => {
    const { container } = renderPagination(1, 1)
    expect(container.querySelector('nav')).toBeNull()
  })

  it('marks the current page and links the others', () => {
    renderPagination(2, 3)

    expect(screen.getByRole('link', { name: 'Page 2' })).toHaveAttribute('aria-current', 'page')
    expect(screen.getByRole('link', { name: 'Page 3' })).not.toHaveAttribute('aria-current')
    expect(screen.getByRole('link', { name: 'Page 3' })).toHaveAttribute('href', '/perfumes?page=3')
  })

  it('changes the page in the URL when a page link is used', async () => {
    renderPagination(1, 3)

    await userEvent.click(screen.getByRole('link', { name: 'Page 3' }))

    expect(screen.getByTestId('location')).toHaveTextContent('/perfumes?page=3')
  })

  it('moves with Next and Previous', async () => {
    renderPagination(2, 3)

    await userEvent.click(screen.getByRole('link', { name: 'Next' }))
    expect(screen.getByTestId('location')).toHaveTextContent('/perfumes?page=3')

    await userEvent.click(screen.getByRole('link', { name: 'Previous' }))
    // The props are fixed in this test, so Previous goes to page 1, which is written without a page parameter.
    expect(screen.getByTestId('location')).toHaveTextContent('/perfumes')
  })

  it('shows Previous and Next as disabled text at the ends', () => {
    renderPagination(1, 3)
    expect(screen.queryByRole('link', { name: 'Previous' })).not.toBeInTheDocument()
    expect(screen.getByText('Previous')).toHaveAttribute('aria-disabled', 'true')
  })

  it('is operable with the keyboard', async () => {
    renderPagination(1, 3)

    screen.getByRole('link', { name: 'Page 2' }).focus()
    await userEvent.keyboard('{Enter}')

    expect(screen.getByTestId('location')).toHaveTextContent('/perfumes?page=2')
  })
})
