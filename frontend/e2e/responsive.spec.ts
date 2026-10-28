import type { Page } from '@playwright/test'
import { expect, test, waitReady, type Role } from './fixtures'

const WIDTHS = [360, 390, 768, 1024, 1440]
const PAGES: [Role, string[]][] = [
  ['fc_owner', ['/fc', '/fc/orders', '/fc/warehouses', '/fc/analytics']],
  ['seller_owner', ['/seller', '/seller/products']],
  ['platform_admin', ['/admin', '/admin/organizations']],
  ['fc_picker', ['/work']],
]

async function layoutIssues(page: Page, mobile: boolean) {
  return page.evaluate((checkTouch) => {
    const de = document.documentElement
    const overflow = de.scrollWidth > de.clientWidth
    const small = checkTouch
      ? [...document.querySelectorAll<HTMLElement>('button, [role=button], a.p-button')]
          .filter((e) => {
            const r = e.getBoundingClientRect()
            return r.width > 0 && r.height > 0 && getComputedStyle(e).visibility !== 'hidden' && (r.height < 44 || r.width < 44)
          })
          .map((e) => `${e.getAttribute('aria-label') ?? e.innerText.trim().slice(0, 20)} ${Math.round(e.getBoundingClientRect().width)}x${Math.round(e.getBoundingClientRect().height)}`)
      : []
    return { overflow, small }
  }, mobile)
}

for (const [role, paths] of PAGES) {
  test(`адаптивность: ${role} без горизонтального скролла, кнопки ≥44px на мобильных`, async ({ page, loginAs }) => {
    await loginAs(role)
    for (const width of WIDTHS) {
      await page.setViewportSize({ width, height: width < 800 ? 800 : 900 })
      for (const path of paths) {
        await page.goto(path)
        await waitReady(page)
        const issues = await layoutIssues(page, width <= 390)
        expect(issues, `${path} @${width}px`).toEqual({ overflow: false, small: [] })
      }
    }
  })
}
