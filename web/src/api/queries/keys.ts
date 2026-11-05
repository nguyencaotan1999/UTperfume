/**
 * Query key factories, one per resource (architecture 5.2). Add `productKeys`, `brandKeys` and so on
 * here in later phases, following the same shape.
 */
export const healthKeys = {
  all: ['health'] as const,
  status: () => [...healthKeys.all, 'status'] as const,
}
