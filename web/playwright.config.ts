import { defineConfig, devices } from '@playwright/test'

const port = 5080
const baseURL = `http://localhost:${port}`

/**
 * The API serves the built SPA from wwwroot (ADR-0012), so the smoke test runs against the real API.
 * `npm run build` output is copied to src/UTperfume.Api/wwwroot by the webServer command.
 * Database: the API itself needs none for /health/live. The connection string, when needed, comes from
 * the environment (ConnectionStrings__Catalog), never from a file.
 */
export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: { baseURL, trace: 'on-first-retry' },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    command:
      'npm run build && node scripts/copy-to-wwwroot.mjs && dotnet run --project ../src/UTperfume.Api -c Release --no-launch-profile --urls ' +
      baseURL,
    url: `${baseURL}/health/live`,
    reuseExistingServer: !process.env.CI,
    timeout: 180_000,
    env: {
      ASPNETCORE_ENVIRONMENT: 'Development',
      ...(process.env.ConnectionStrings__Catalog ? { ConnectionStrings__Catalog: process.env.ConnectionStrings__Catalog } : {}),
    },
  },
})
