import { expect, test, toast, type Role } from './fixtures'

const CASES: [Role, string, RegExp][] = [
  ['seller_owner', '/fc/staff', /^\/seller$/],
  ['seller_manager', '/admin/users', /^\/seller$/],
  ['fc_owner', '/admin/moderation', /^\/fc$/],
  ['fc_supervisor', '/fc/billing', /^\/fc$/],
  ['platform_admin', '/seller/orders', /^\/admin$/],
]

for (const [role, path, home] of CASES) {
  test(`RBAC: ${role} → ${path} — редирект на главную и тост`, async ({ page, loginAs }) => {
    await loginAs(role)
    await page.goto(path)
    await expect(toast(page).filter({ hasText: 'Нет доступа к разделу' })).toBeVisible()
    await expect.poll(() => new URL(page.url()).pathname).toMatch(home)
    await expect(page).not.toHaveURL(/denied=/)
  })
}

test('RBAC: гость → /login?redirect, после входа возвращает на страницу', async ({ page }) => {
  await page.goto('/fc/orders')
  await expect(page).toHaveURL(/\/login\?redirect=/)
  await page.getByLabel('Email').fill('director@demo.ru')
  await page.getByLabel('Пароль').fill('demo')
  await page.getByRole('button', { name: 'Войти', exact: true }).click()
  await expect(page).toHaveURL(/\/fc\/orders$/)
})

test('RBAC: организация на модерации видит только экран ожидания', async ({ page, loginAs }) => {
  await loginAs('pending')
  await page.goto('/seller/orders')
  await expect(page).toHaveURL(/\/pending$/)
})
