import { expect, test } from '@playwright/test'

test('the SPA loads and reports the service status', async ({ page }) => {
  await page.goto('/')

  await expect(page.getByRole('heading', { name: 'UTperfume' })).toBeVisible()
  await expect(page.getByText('Service status: ok')).toBeVisible()
})

test('/health/live returns 200', async ({ request }) => {
  const response = await request.get('/health/live')

  expect(response.status()).toBe(200)
})

test('an unknown client route still serves the SPA, an unknown API route is a 404 problem', async ({ page, request }) => {
  await page.goto('/some/unknown/route')
  await expect(page.getByRole('heading', { name: 'UTperfume' })).toBeVisible()

  const response = await request.get('/api/v1/does-not-exist')
  expect(response.status()).toBe(404)
  expect(response.headers()['content-type']).toContain('application/problem+json')
})
