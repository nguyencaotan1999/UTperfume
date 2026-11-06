import { describe, expect, it } from 'vitest'
import { formatVnd } from './formatVnd'

import { plain } from '../../test/text'

describe('formatVnd', () => {
  it('formats whole dong with dot separators and no decimals', () => {
    expect(plain(formatVnd(1_250_000))).toBe('1.250.000 ₫')
  })

  it('formats zero', () => {
    expect(plain(formatVnd(0))).toBe('0 ₫')
  })

  it('formats large values up to the filter limit', () => {
    expect(plain(formatVnd(8_900_000))).toBe('8.900.000 ₫')
    expect(plain(formatVnd(10_000_000_000))).toBe('10.000.000.000 ₫')
  })

  it('formats small values', () => {
    expect(plain(formatVnd(999))).toBe('999 ₫')
    expect(plain(formatVnd(1_000))).toBe('1.000 ₫')
  })

  it('never shows decimals, even for a fractional input', () => {
    expect(plain(formatVnd(1_250_000.4))).toBe('1.250.000 ₫')
    expect(plain(formatVnd(1_250_000.6))).toBe('1.250.001 ₫')
  })

  it('does not show a minus sign for negative zero', () => {
    expect(plain(formatVnd(-0))).toBe('0 ₫')
  })

  it('rejects values that are not finite numbers', () => {
    expect(() => formatVnd(Number.NaN)).toThrow(RangeError)
    expect(() => formatVnd(Number.POSITIVE_INFINITY)).toThrow(RangeError)
  })
})
