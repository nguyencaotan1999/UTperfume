import type { AvailabilityStatus } from '../../api/types'
import { availabilityLabels, availabilityModifier } from './availability'

export function AvailabilityBadge({ status }: { status: AvailabilityStatus }) {
  return <span className={`badge badge--${availabilityModifier[status]}`}>{availabilityLabels[status]}</span>
}
