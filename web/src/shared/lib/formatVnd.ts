const formatter = new Intl.NumberFormat('vi-VN', {
  style: 'currency',
  currency: 'VND',
  maximumFractionDigits: 0,
})

/**
 * Formats whole dong as `1.250.000 ₫` (architecture section 5.2). The API sends integers, so no decimals are shown.
 * Intl separates the number and the symbol with a no-break space.
 */
export function formatVnd(amount: number): string {
  if (!Number.isFinite(amount)) throw new RangeError('formatVnd needs a finite number')
  return formatter.format(Math.round(amount) + 0) // "+ 0" turns -0 into 0
}
