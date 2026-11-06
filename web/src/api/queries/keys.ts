/**
 * Query key factories, one per resource (architecture 5.2).
 */
export const homeKeys = {
  all: ['home'] as const,
}

export const filterKeys = {
  all: ['filters'] as const,
}

export const productKeys = {
  all: ['products'] as const,
  lists: () => [...productKeys.all, 'list'] as const,
  list: (params: object) => [...productKeys.all, 'list', params] as const,
  detail: (slug: string) => [...productKeys.all, 'detail', slug] as const,
  related: (slug: string) => [...productKeys.all, 'related', slug] as const,
}

export const brandKeys = {
  all: ['brands'] as const,
  list: () => [...brandKeys.all, 'list'] as const,
  detail: (slug: string) => [...brandKeys.all, 'detail', slug] as const,
}

export const categoryKeys = {
  all: ['categories'] as const,
  detail: (slug: string) => [...categoryKeys.all, 'detail', slug] as const,
}
