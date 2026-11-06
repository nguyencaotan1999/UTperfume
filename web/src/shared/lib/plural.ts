/** "1 perfume", "12 perfumes". */
export function perfumeCount(count: number): string {
  return `${count} ${count === 1 ? 'perfume' : 'perfumes'}`
}
