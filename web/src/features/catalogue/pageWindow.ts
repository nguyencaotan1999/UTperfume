/** Page numbers to show: the first, the last and a window around the current page, with gaps marked. */
export function pageWindow(page: number, totalPages: number): Array<number | 'gap'> {
  const wanted = new Set([1, totalPages, page - 1, page, page + 1])
  const numbers = [...wanted].filter((n) => n >= 1 && n <= totalPages).sort((a, b) => a - b)
  const result: Array<number | 'gap'> = []
  numbers.forEach((n, index) => {
    const previous = numbers[index - 1]
    if (previous !== undefined && n - previous > 1) result.push('gap')
    result.push(n)
  })
  return result
}
