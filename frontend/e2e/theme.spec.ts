import { expect, test, waitReady } from './fixtures'

test('тема: переключение светлая/тёмная сохраняется после перезагрузки', async ({ page, loginAs }) => {
  await loginAs('fc_owner')
  const html = page.locator('html')
  const startDark = await html.evaluate((e) => e.classList.contains('app-dark'))
  const toggle = page.getByRole('button', { name: startDark ? 'Светлая тема' : 'Тёмная тема' })
  await toggle.click()
  await expect(html).toHaveClass(startDark ? /^(?!.*app-dark)/ : /app-dark/)
  await page.reload()
  await waitReady(page)
  await expect(html).toHaveClass(startDark ? /^(?!.*app-dark)/ : /app-dark/)
  for (const path of ['/fc/orders', '/fc/analytics']) {
    await page.goto(path)
    await waitReady(page)
  }
})

test('тема: тёмная по системной настройке на странице входа', async ({ browser }) => {
  const ctx = await browser.newContext({ colorScheme: 'dark' })
  const page = await ctx.newPage()
  await page.goto('/login')
  await expect(page.locator('html')).toHaveClass(/app-dark/)
  await ctx.close()
})
