import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, renderHook } from '@testing-library/react'
import type { ReactElement, ReactNode } from 'react'
import { MemoryRouter } from 'react-router'

export function createTestQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
  })
}

function wrapperFor(client: QueryClient, route: string) {
  return function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={client}>
        <MemoryRouter initialEntries={[route]}>{children}</MemoryRouter>
      </QueryClientProvider>
    )
  }
}

/** Renders with a fresh QueryClient (retry off) and a MemoryRouter. */
export function renderWithProviders(ui: ReactElement, options: { route?: string } = {}) {
  const client = createTestQueryClient()
  return { client, ...render(ui, { wrapper: wrapperFor(client, options.route ?? '/') }) }
}

export function renderHookWithProviders<T>(hook: () => T, options: { route?: string } = {}) {
  const client = createTestQueryClient()
  return { client, ...renderHook(hook, { wrapper: wrapperFor(client, options.route ?? '/') }) }
}
