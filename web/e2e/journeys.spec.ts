import { expect, test, type Page } from '@playwright/test'

/** P3-06 journeys against the real API and the seeded database. */

const isProductsList = (url: string) => new URL(url).pathname === '/api/v1/products'
const isProductDetail = (url: string) => /^\/api\/v1\/products\/[^/]+$/.test(new URL(url).pathname)

const list = (page: Page) => page.getByRole('list', { name: 'Perfumes' })

async function firstCardName(page: Page): Promise<string> {
  const name = await list(page).getByRole('heading', { level: 3 }).first().textContent()
  return (name ?? '').trim()
}

test('home page to product page: the card opens the matching product', async ({ page }) => {
  await page.goto('/')
  const card = page.getByRole('region', { name: 'New arrivals' }).getByRole('article').first()
  await expect(card).toBeVisible()
  const name = ((await card.getByRole('heading', { level: 3 }).textContent()) ?? '').trim()
  expect(name).not.toBe('')

  const detail = page.waitForResponse((r) => isProductDetail(r.url()) && r.request().method() === 'GET')
  await card.getByRole('link').first().click()

  expect((await detail).status()).toBe(200)
  await expect(page).toHaveURL(/\/perfumes\/[a-z0-9-]+$/)
  await expect(page.getByRole('heading', { level: 1, name })).toBeVisible()
  await expect(page.getByRole('group', { name: 'Size' })).toBeVisible()
})

test('a filter updates the URL and browser back restores the previous filter state', async ({ page }) => {
  await page.goto('/perfumes')
  await expect(list(page).getByRole('article').first()).toBeVisible()

  const brand = page.getByRole('group', { name: 'Brand' }).getByRole('checkbox').first()
  const concentration = page.getByRole('checkbox', { name: 'Eau de Parfum' })

  await brand.click()
  await expect(brand).toBeChecked()
  await expect(page).toHaveURL(/[?&]brand=/)
  await expect(page).not.toHaveURL(/concentration=/)
  const brandUrl = page.url()

  await concentration.click()
  await expect(concentration).toBeChecked()
  await expect(page).toHaveURL(/concentration=EDP/)
  await expect(page).toHaveURL(/[?&]brand=/)

  await page.goBack()
  await expect(page).toHaveURL(brandUrl)
  await expect(brand).toBeChecked()
  await expect(concentration).not.toBeChecked()
  await expect(list(page).getByRole('article').first()).toBeVisible()

  await page.goBack()
  await expect(page).toHaveURL(/\/perfumes$/)
  await expect(brand).not.toBeChecked()
  await expect(page.getByRole('alert')).toHaveCount(0)
})

test('pagination moves between pages through the URL and shows different products', async ({ page }) => {
  await page.goto('/perfumes?pageSize=12&sort=name_asc')
  await expect(list(page).getByRole('article').first()).toBeVisible()
  const pagination = page.getByRole('navigation', { name: 'Pagination' })
  await expect(pagination).toBeVisible()
  const firstOnPageOne = await firstCardName(page)

  const second = page.waitForResponse((r) => isProductsList(r.url()) && new URL(r.url()).searchParams.get('page') === '2')
  await pagination.getByRole('link', { name: 'Page 2' }).click()

  expect((await second).status()).toBe(200)
  await expect(page).toHaveURL(/[?&]page=2/)
  await expect(pagination.getByRole('link', { name: 'Page 2' })).toHaveAttribute('aria-current', 'page')
  await expect.poll(() => firstCardName(page)).not.toBe(firstOnPageOne)

  await pagination.getByRole('link', { name: 'Previous' }).click()
  await expect(pagination.getByRole('link', { name: 'Page 1' })).toHaveAttribute('aria-current', 'page')
  await expect.poll(() => firstCardName(page)).toBe(firstOnPageOne)
})

test('an unknown product slug shows the not found page', async ({ page }) => {
  const detail = page.waitForResponse((r) => isProductDetail(r.url()) && r.request().method() === 'GET')
  await page.goto('/perfumes/this-product-does-not-exist')

  expect((await detail).status()).toBe(404)
  await expect(page.getByRole('heading', { level: 1, name: 'Page not found' })).toBeVisible()
  await expect(page.getByRole('navigation', { name: 'Main' })).toBeVisible()
})

test('the product page shows the availability label and no stock quantity', async ({ page }) => {
  await page.goto('/perfumes')
  const link = list(page).getByRole('article').first().getByRole('link').first()
  await expect(link).toBeVisible()

  const detail = page.waitForResponse((r) => isProductDetail(r.url()) && r.request().method() === 'GET')
  await link.click()
  const response = await detail
  expect(response.status()).toBe(200)

  // The contract carries only the availability status, never a quantity.
  const keys: string[] = []
  const collect = (value: unknown) => {
    if (Array.isArray(value)) value.forEach(collect)
    else if (value && typeof value === 'object') {
      for (const [key, inner] of Object.entries(value)) {
        keys.push(key)
        collect(inner)
      }
    }
  }
  collect(await response.json())
  expect(keys.filter((key) => /quantity|onhand|stockcount|unitsinstock/i.test(key))).toEqual([])

  const label = /^(In stock|Low stock|Out of stock)$/
  const sizeGroup = page.getByRole('group', { name: 'Size' })
  const radios = sizeGroup.getByRole('radio')
  const badge = page.locator('.variants__selected .badge')
  const expectNoQuantity = async () => {
    const text = (await page.locator('main').innerText()).toLowerCase()
    expect(text).not.toContain('quantity')
    expect(text).not.toMatch(/\bstock\s*:/)
    expect(text).not.toMatch(/\b(in stock|low stock|out of stock|available|availability)\s*\(\s*\d+\s*\)/)
    expect(text).not.toMatch(/\(\s*\d+\s*\)\s*(in stock|low stock|out of stock|available)/)
    expect(text).not.toMatch(/\b\d+\s+items?\b/)
    expect(text).not.toMatch(/\b\d+\s+(left|remaining|available|in stock|units?|pcs|pieces)\b/)
    expect(text).not.toMatch(/\b(only|just)\s+\d+\b/)
  }

  const optionCount = await radios.count()
  expect(optionCount).toBeGreaterThan(0)
  const badges = sizeGroup.locator('.badge')
  expect(await badges.count()).toBe(optionCount)
  for (const option of await badges.all()) {
    await expect(option).toHaveText(label)
  }

  // Every variant, not only the default, shows a label and no quantity.
  for (let i = 0; i < optionCount; i++) {
    await radios.nth(i).check({ force: true })
    await expect(radios.nth(i)).toBeChecked()
    await expect(badge).toHaveText(label)
    await expectNoQuantity()
  }
})
