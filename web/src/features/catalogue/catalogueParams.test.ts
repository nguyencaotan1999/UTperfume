import { describe, expect, it } from 'vitest'
import {
  applyChange,
  canonicalSearch,
  emptyParams,
  parseParams,
  parsePositiveInt,
  toQuery,
  toSearchParams,
  toggleValue,
  validatePrice,
  validatePriceRange,
  validateSearchText,
  sameVolume,
} from './catalogueParams'
import { pageWindow } from './pageWindow'

const parse = (search: string) => parseParams(new URLSearchParams(search))

describe('URL round trip', () => {
  const full =
    'q=sauv&brand=dior&brand=chanel&category=men&concentration=EDP&volume=50&volume=100&minPrice=1000000&maxPrice=5000000&pageSize=12&page=2&sort=price_asc'

  it('restores every parameter from the URL', () => {
    expect(parse(full)).toEqual({
      q: 'sauv',
      brand: ['dior', 'chanel'],
      category: ['men'],
      concentration: ['EDP'],
      volume: ['50', '100'],
      minPrice: '1000000',
      maxPrice: '5000000',
      sort: 'price_asc',
      page: '2',
      pageSize: '12',
    })
  })

  it('writes the same state back to the URL', () => {
    const written = toSearchParams(parse(full))

    expect(new URLSearchParams(written.toString()).getAll('brand')).toEqual(['dior', 'chanel'])
    expect(parseParams(written)).toEqual(parse(full))
  })

  it('omits defaults and empty values so URLs stay canonical', () => {
    expect(toSearchParams({ ...emptyParams, sort: 'newest', page: '1', pageSize: '24', q: '   ' }).toString()).toBe('')
  })

  it('keeps repeated values for multi-value filters in order', () => {
    expect(toSearchParams({ ...emptyParams, brand: ['b', 'a'] }).toString()).toBe('brand=b&brand=a')
  })

  it('ignores empty values in a list', () => {
    expect(parse('brand=&brand=dior').brand).toEqual(['dior'])
  })
})

describe('toQuery', () => {
  it('sends numbers for numeric text and arrays for multi-value filters', () => {
    const query = toQuery(parse('minPrice=1000000&page=2&volume=30.5&brand=dior&brand=chanel'))

    expect(query).toEqual({ minPrice: 1_000_000, page: 2, volume: [30.5], brand: ['dior', 'chanel'] })
  })

  it('passes invalid text through so the API answers with a 400 field error', () => {
    const query = toQuery(parse('minPrice=1500000.5&page=abc&sort=cheapest')) as Record<string, unknown>

    expect(query.minPrice).toBe('1500000.5')
    expect(query.page).toBe('abc')
    expect(query.sort).toBe('cheapest')
  })

  it('sends nothing for an empty state', () => {
    expect(toQuery(emptyParams)).toEqual({})
  })
})

describe('canonicalSearch', () => {
  it('drops the sort but keeps filters and page (ADR-0006)', () => {
    expect(canonicalSearch(parse('brand=dior&sort=price_asc&page=2'))).toBe('?brand=dior&page=2')
  })

  it('is empty for the unfiltered catalogue', () => {
    expect(canonicalSearch(parse('sort=price_desc'))).toBe('')
  })
})

describe('applyChange', () => {
  it('resets to page 1 when a filter changes', () => {
    expect(applyChange(parse('page=3'), { brand: ['dior'] }).page).toBe('')
  })

  it('keeps an explicit page when the change is a page change', () => {
    expect(applyChange(parse('page=3'), { page: '4' }).page).toBe('4')
  })

  it('can keep the current page on request', () => {
    expect(applyChange(parse('page=3'), { sort: 'name_asc' }, { keepPage: true }).page).toBe('3')
  })
})

describe('toggleValue', () => {
  it('adds a missing value and removes a present one', () => {
    expect(toggleValue(['a'], 'b')).toEqual(['a', 'b'])
    expect(toggleValue(['a', 'b'], 'a')).toEqual(['b'])
  })

  it('compares volumes numerically', () => {
    expect(toggleValue(['50'], '50.0', sameVolume)).toEqual([])
  })
})

describe('parsePositiveInt', () => {
  it.each([
    ['3', 3],
    ['1', 1],
    ['0', 1],
    ['-2', 1],
    ['1.5', 1],
    ['abc', 1],
    ['', 1],
  ])('parses %j as %i', (text, expected) => {
    expect(parsePositiveInt(text, 1)).toBe(expected)
  })
})

describe('client-side validation', () => {
  it.each([
    ['', null],
    ['  ', null],
    ['a', 'Enter at least 2 characters.'],
    ['ab', null],
    ['x'.repeat(100), null],
    ['x'.repeat(101), 'Enter at most 100 characters.'],
  ])('validates the search text %j', (text, expected) => {
    expect(validateSearchText(text)).toBe(expected)
  })

  it.each(['1500000.5', '1.500.000', '-5', 'abc', '1e6', '1,5'])('rejects the price %j (whole dong only)', (text) => {
    expect(validatePrice(text)).toMatch(/whole dong/)
  })

  it.each(['', '0', '1250000', '10000000000'])('accepts the price %j', (text) => {
    expect(validatePrice(text)).toBeNull()
  })

  it('rejects a price above the limit', () => {
    expect(validatePrice('10000000001')).toMatch(/at most/)
  })

  it('requires the minimum to be at most the maximum', () => {
    expect(validatePriceRange('100', '50')).toMatch(/must not be above/)
    expect(validatePriceRange('50', '50')).toBeNull()
    expect(validatePriceRange('', '50')).toBeNull()
    expect(validatePriceRange('100', '')).toBeNull()
  })
})

describe('pageWindow', () => {
  it('lists every page when there are few', () => {
    expect(pageWindow(2, 4)).toEqual([1, 2, 3, 4])
  })

  it('collapses the middle of a long list around the current page', () => {
    expect(pageWindow(10, 20)).toEqual([1, 'gap', 9, 10, 11, 'gap', 20])
  })

  it('has no gap next to the ends', () => {
    expect(pageWindow(2, 20)).toEqual([1, 2, 3, 'gap', 20])
    expect(pageWindow(19, 20)).toEqual([1, 'gap', 18, 19, 20])
  })

  it('handles a single page', () => {
    expect(pageWindow(1, 1)).toEqual([1])
  })
})
