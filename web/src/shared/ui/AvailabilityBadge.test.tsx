import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { AvailabilityBadge } from './AvailabilityBadge'

describe('AvailabilityBadge', () => {
  it.each([
    ['InStock', 'In stock'],
    ['LowStock', 'Low stock'],
    ['OutOfStock', 'Out of stock'],
  ] as const)('shows the %s state as "%s"', (status, label) => {
    render(<AvailabilityBadge status={status} />)

    expect(screen.getByText(label)).toBeInTheDocument()
  })

  it.each(['InStock', 'LowStock', 'OutOfStock'] as const)('never shows a digit or a quantity for %s', (status) => {
    const { container } = render(<AvailabilityBadge status={status} />)

    expect(container.textContent).not.toMatch(/\d/)
    expect(container.textContent?.toLowerCase()).not.toContain('quantity')
    expect(container.textContent?.toLowerCase()).not.toContain('left')
  })

  it('accepts only the status, so there is no way to pass a quantity', () => {
    // @ts-expect-error a quantity prop does not exist on the component
    render(<AvailabilityBadge status="LowStock" quantity={3} />)

    expect(screen.queryByText(/3/)).not.toBeInTheDocument()
  })
})
