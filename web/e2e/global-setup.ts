import type { FullConfig } from '@playwright/test'

/**
 * The E2E run needs real data, so a broken data flow fails the run instead of showing error states that still pass.
 * The API (Development) migrates the database named in ConnectionStrings__Catalog and seeds the sample catalogue in
 * the background after it starts. This waits until the seed is visible through the public API.
 *
 * ConnectionStrings__Catalog comes from the environment only, for example a throwaway database on LocalDB, SQL
 * Express or the CI SQL Server container. It is never written to a file.
 */
export default async function globalSetup(config: FullConfig) {
  if (!process.env.ConnectionStrings__Catalog) {
    throw new Error(
      'ConnectionStrings__Catalog is not set. The E2E tests need a database with the development seed ' +
        '(see docs/dev-setup.md, "Running the E2E tests").',
    )
  }

  const baseURL = config.projects[0]?.use.baseURL
  if (!baseURL) throw new Error('No baseURL configured.')

  const waitSeconds = Number(process.env.E2E_SEED_WAIT_SECONDS ?? '120')
  const deadline = Date.now() + waitSeconds * 1000
  let last = 'no response'

  while (Date.now() < deadline) {
    try {
      const response = await fetch(`${baseURL}/api/v1/catalog/filters`)
      if (response.ok) {
        const filters = (await response.json()) as { priceRange: unknown }
        if (filters.priceRange) return
        last = 'the API answers but the catalogue is still empty'
      } else {
        last = `HTTP ${response.status}`
      }
    } catch (error) {
      last = error instanceof Error ? error.message : String(error)
    }
    await new Promise((resolve) => setTimeout(resolve, 1000))
  }

  throw new Error(`The seeded catalogue did not become available within ${waitSeconds}s (${last}).`)
}
