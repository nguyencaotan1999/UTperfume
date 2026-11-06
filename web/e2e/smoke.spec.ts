import { expect, test, type Page } from '@playwright/test'

/** Waits for the API call the page makes, so the test fails if the call fails. */
const apiResponse = (page: Page, path: string) =>
  page.waitForResponse((response) => new URL(response.url()).pathname === path && response.request().method() === 'GET')

async function expectNoErrorState(page: Page) {
  await expect(page.getByText(/could not be loaded/i)).toHaveCount(0)
  await expect(page.getByRole('alert')).toHaveCount(0)
}

test('the home page loads data from the API and shows seeded products', async ({ page }) => {
  const home = apiResponse(page, '/api/v1/home')
  await page.goto('/')

  expect((await home).status()).toBe(200)
  await expect(page.getByRole('heading', { level: 1, name: 'Find your signature fragrance' })).toBeVisible()
  await expect(page.getByRole('navigation', { name: 'Main' })).toBeVisible()
  await expect(page.getByRole('region', { name: 'New arrivals' }).getByRole('article').first()).toBeVisible()
  await expectNoErrorState(page)
})

test('the catalogue loads products and filters from the API and filters through the URL', async ({ page }) => {
  const products = apiResponse(page, '/api/v1/products')
  const filters = apiResponse(page, '/api/v1/catalog/filters')
  await page.goto('/perfumes')

  expect((await products).status()).toBe(200)
  expect((await filters).status()).toBe(200)
  await expect(page.getByRole('heading', { level: 1, name: 'Perfumes' })).toBeVisible()
  await expect(page.getByRole('list', { name: 'Perfumes' }).getByRole('article').first()).toBeVisible()
  await expectNoErrorState(page)

  const brand = page.getByRole('group', { name: 'Brand' }).getByRole('checkbox').first()
  const filtered = apiResponse(page, '/api/v1/products')
  await brand.click() // the URL update is asynchronous, so assert the state afterwards instead of check()
  await expect(brand).toBeChecked()
  expect((await filtered).status()).toBe(200)
  await expect(page).toHaveURL(/[?&]brand=/)
  await expect(page.getByRole('list', { name: 'Perfumes' }).getByRole('article').first()).toBeVisible()
  await expectNoErrorState(page)
})

test('a shared catalogue URL restores its filters', async ({ page }) => {
  const products = apiResponse(page, '/api/v1/products')
  await page.goto('/perfumes?concentration=EDP&sort=price_asc&pageSize=12')

  expect((await products).status()).toBe(200)
  await expect(page.getByLabel('Sort by')).toHaveValue('price_asc')
  await expect(page.getByLabel('Per page')).toHaveValue('12')
  await expect(page.getByRole('checkbox', { name: 'Eau de Parfum' })).toBeChecked()
  await expectNoErrorState(page)
})

test('/health/live returns 200', async ({ request }) => {
  const response = await request.get('/health/live')

  expect(response.status()).toBe(200)
})

test('the public API answers 200 for the data the pages use', async ({ request }) => {
  for (const path of ['/api/v1/home', '/api/v1/products', '/api/v1/catalog/filters']) {
    expect((await request.get(path)).status(), path).toBe(200)
  }
})

test('an unknown client route shows the not found page, an unknown API route is a 404 problem', async ({ page, request }) => {
  await page.goto('/some/unknown/route')
  await expect(page.getByRole('heading', { level: 1, name: 'Page not found' })).toBeVisible()

  const response = await request.get('/api/v1/does-not-exist')
  expect(response.status()).toBe(404)
  expect(response.headers()['content-type']).toContain('application/problem+json')
})

test('a product card opens the product page with data from the API', async ({ page }) => {
  await page.goto('/')
  const card = page.getByRole('region', { name: 'New arrivals' }).getByRole('article').first()
  await expect(card).toBeVisible()
  const detail = page.waitForResponse(
    (response) => /^\/api\/v1\/products\/[^/]+$/.test(new URL(response.url()).pathname) && response.request().method() === 'GET',
  )

  await card.getByRole('link').first().click()

  expect((await detail).status()).toBe(200)
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
  await expect(page.getByRole('group', { name: 'Size' })).toBeVisible()
  await expect(page).toHaveTitle(/ – .* \| UTperfume$/)
  await expectNoErrorState(page)
})

test('the brand list opens a brand page that lists its products', async ({ page }) => {
  const brands = apiResponse(page, '/api/v1/brands')
  await page.goto('/brands')
  expect((await brands).status()).toBe(200)

  const link = page.getByRole('list', { name: 'Brands' }).getByRole('link').first()
  await expect(link).toBeVisible()
  const brand = page.waitForResponse(
    (response) => /^\/api\/v1\/brands\/[^/]+$/.test(new URL(response.url()).pathname) && response.request().method() === 'GET',
  )
  await link.click()

  expect((await brand).status()).toBe(200)
  await expect(page).toHaveURL(/\/brands\/[a-z0-9-]+$/)
  await expectNoErrorState(page)
})
