import type { AvailabilityStatus } from '../../api/types'

/** The three public stock states and their labels. The UI never shows a quantity (BR-08, AC-P2b). */
export const availabilityLabels: Record<AvailabilityStatus, string> = {
  InStock: 'In stock',
  LowStock: 'Low stock',
  OutOfStock: 'Out of stock',
}

export const availabilityModifier: Record<AvailabilityStatus, string> = {
  InStock: 'in',
  LowStock: 'low',
  OutOfStock: 'out',
}
