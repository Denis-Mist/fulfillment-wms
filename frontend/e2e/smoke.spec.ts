import { ACCOUNTS, expect, test, waitReady, type Role } from './fixtures'

const HOME: Record<Role, RegExp> = {
  fc_owner: /^\/fc$/,
  fc_manager: /^\/fc$/,
  fc_supervisor: /^\/fc$/,
  fc_picker: /^\/work/,
  fc_receiver: /^\/work/,
  fc_packer: /^\/work/,
  seller_owner: /^\/seller$/,
  seller_manager: /^\/seller$/,
  platform_admin: /^\/admin$/,
  pending: /^\/pending$/,
}

for (const role of Object.keys(ACCOUNTS) as Role[]) {
  test(`smoke: ${role} — все пункты меню открываются без ошибок`, async ({ page, loginAs }) => {
    await loginAs(role)
    expect(new URL(page.url()).pathname).toMatch(HOME[role])
    if (role === 'pending') {
      await expect(page.getByRole('heading', { level: 1, name: 'Заявка на проверке' })).toBeVisible()
      return
    }
    const links = await page.locator('aside nav a[href], main ~ nav a[href], nav a[href]').evaluateAll((els) => [
      ...new Set(els.map((e) => e.getAttribute('href')!).filter((h) => h.startsWith('/'))),
    ])
    expect(links.length).toBeGreaterThan(0)
    for (const href of links) {
      await page.goto(href)
      expect(new URL(page.url()).pathname, `${href} не должен редиректить`).toBe(href)
      await waitReady(page)
      if (href.startsWith('/work')) {
        // Кабинет работника (ТСД): заголовок экрана в шапке, h1 нет — проверяем шапку и содержимое
        await expect(page.getByRole('banner')).toContainText(/Мои задания|Сканер/)
        await expect(page.getByRole('main')).not.toBeEmpty()
      } else await expect(page.locator('main h1').first()).toBeVisible()
      await expect(page.getByText('Не удалось загрузить данные')).toHaveCount(0)
    }
  })
}
