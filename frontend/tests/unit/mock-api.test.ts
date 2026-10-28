import { describe, expect, it } from 'vitest'
import { api, login, useMockServer } from './setup-msw'

useMockServer()

const uid = () => Math.random().toString(36).slice(2, 6).toUpperCase()

describe('мок-API: авторизация', () => {
  it.each([
    'director@demo.ru',
    'manager@demo.ru',
    'staff8@fc1.demo.ru',
    'picker@demo.ru',
    'seller@demo.ru',
    'manager@ud.demo.ru',
    'admin@demo.ru',
    'owner1@pending.demo.ru',
  ])('вход %s', async (email) => {
    const { accessToken } = await login(email)
    const me = await api('/auth/me', { token: accessToken })
    expect(me.status).toBe(200)
    expect(me.body.email).toBe(email)
  })

  it('неверный пароль → 401 с понятным текстом', async () => {
    const res = await api('/auth/login', { method: 'POST', json: { email: 'director@demo.ru', password: 'nope' } })
    expect(res.status).toBe(401)
    expect(res.body.title).toBe('Неверный email или пароль')
  })

  it('email без учёта регистра и пробелов', async () => {
    const res = await api('/auth/login', { method: 'POST', json: { email: '  Director@Demo.RU ', password: 'demo' } })
    expect(res.status).toBe(200)
  })

  it('без токена → 401', async () => {
    expect((await api('/auth/me')).status).toBe(401)
    expect((await api('/orders')).status).toBe(401)
  })

  it('регистрация: обязательные поля, дубль email, новая организация на модерации', async () => {
    expect((await api('/auth/register', { method: 'POST', json: { user: {}, organization: {} } })).status).toBe(400)
    const body = (email: string) => ({
      organizationType: 'seller',
      organization: { name: 'ООО Тест', inn: '7707083893', city: 'Москва', marketplaces: ['wb'] },
      user: { fullName: 'Тест Тестов', email, phone: '+79000000000', password: 'secret1' },
    })
    expect((await api('/auth/register', { method: 'POST', json: body('seller@demo.ru') })).status).toBe(409)
    const email = `qa.${uid().toLowerCase()}@test.ru`
    const res = await api('/auth/register', { method: 'POST', json: body(email) })
    expect(res.status).toBe(201)
    expect(res.body.user.memberships[0].role).toBe('seller_owner')
    expect(res.body.user.memberships[0].organizationStatus).toBe('pending')
    // Организация на модерации: рабочие данные закрыты
    expect((await api('/orders', { token: res.body.accessToken })).status).toBe(403)
  })
})

describe('мок-API: изоляция данных по организациям', () => {
  it('селлер видит только свои заказы', async () => {
    const { accessToken, user } = await login('seller@demo.ru')
    const res = await api('/orders?pageSize=200', { token: accessToken })
    expect(res.status).toBe(200)
    expect(res.body.items.length).toBeGreaterThan(0)
    const own = user.memberships[0]!.organizationId
    expect(res.body.items.every((o: { sellerId: string }) => o.sellerId === own)).toBe(true)
  })

  it('пагинация: форма {items,total,page,pageSize}', async () => {
    const { accessToken } = await login('director@demo.ru')
    const res = await api('/orders?page=2&pageSize=5', { token: accessToken })
    expect(res.body).toMatchObject({ page: 2, pageSize: 5 })
    expect(res.body.items).toHaveLength(5)
    expect(res.body.total).toBeGreaterThan(10)
  })

  it('платформенные справочники — только администратору', async () => {
    const { accessToken } = await login('director@demo.ru')
    expect((await api('/admin/references', { token: accessToken })).status).toBe(403)
  })
})

describe('мок-API: склад и справочники', () => {
  async function firstWarehouse(email: string) {
    const { accessToken } = await login(email)
    const list = await api('/warehouses', { token: accessToken })
    return { token: accessToken, id: list.body[0].id as string }
  }

  it('зона: валидация кода, создание, дубль 409', async () => {
    const { token, id } = await firstWarehouse('director@demo.ru')
    const post = (code: string) => api(`/warehouses/${id}/zones`, { method: 'POST', token, json: { code, name: 'Тестовая', type: 'storage' } })
    expect((await post('зона')).status).toBe(400)
    expect((await post('A')).status).toBe(400)
    const code = `Q${uid().slice(0, 3)}`
    const created = await post(code.toLowerCase())
    expect(created.status).toBe(201)
    expect(created.body.code).toBe(code)
    expect((await post(code)).status).toBe(409)
  })

  it('зону не может создать старший смены (403)', async () => {
    const { token, id } = await firstWarehouse('staff8@fc1.demo.ru')
    const res = await api(`/warehouses/${id}/zones`, { method: 'POST', token, json: { code: 'QX1', name: 'X', type: 'storage' } })
    expect(res.status).toBe(403)
  })

  it('ячейка: проход, вместимость, код A-01-1 и дубль', async () => {
    const { token, id } = await firstWarehouse('director@demo.ru')
    const zones = await api(`/warehouses/${id}/zones`, { token })
    const zoneId = zones.body[0].id
    const post = (aisle: string, capacityLiters = 100) =>
      api(`/warehouses/${id}/locations`, { method: 'POST', token, json: { zoneId, aisle, rack: 97, level: 3, capacityLiters } })
    expect((await post('1')).status).toBe(400)
    expect((await post('ABC')).status).toBe(400)
    expect((await post('Z', 0)).status).toBe(400)
    const created = await post('zz')
    expect(created.status).toBe(201)
    expect(created.body.code).toBe('ZZ-97-3')
    expect((await post('ZZ')).status).toBe(409)
  })

  it('справочник: код, название, дубль', async () => {
    const { accessToken: token } = await login('admin@demo.ru')
    const list = await api('/admin/references', { token })
    const kind = list.body[0].kind
    const post = (code: string, name = 'Тест') => api('/admin/references', { method: 'POST', token, json: { kind, code, name } })
    expect((await post('x')).status).toBe(400)
    expect((await post('ОК')).status).toBe(400)
    expect((await post('QA_OK', '  ')).status).toBe(400)
    const code = `QA_${uid()}`
    expect((await post(code)).status).toBe(201)
    expect((await post(code.toLowerCase())).status).toBe(409)
  })
})
