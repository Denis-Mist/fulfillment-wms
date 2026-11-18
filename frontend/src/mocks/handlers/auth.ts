import { http, HttpResponse } from 'msw'
import type { LoginRequest, RegisterRequest } from '@/modules/auth/types'
import { db } from '../db/store'
import { handleBodyErrors, readJson, latency, problem, tokenFor, withAuth } from '../lib/http'
import { toCurrentUser } from '../lib/mappers'
import { api } from './api'

/** В мок-режиме демо всегда доступно; тесты могут выключить флаг. */
export const demoFlags = { enabled: true }

export const DEMO_PERSONAS: Record<string, string> = {
  platform_admin: 'admin@demo.ru',
  fc_owner: 'director@demo.ru',
  fc_manager: 'manager@demo.ru',
  fc_supervisor: 'staff8@fc1.demo.ru',
  fc_account_manager: 'am@fc1.demo.ru',
  fc_receiver: 'staff1@fc1.demo.ru',
  fc_picker: 'picker@demo.ru',
  fc_packer: 'staff6@fc1.demo.ru',
  seller_owner: 'seller@demo.ru',
  seller_manager: 'manager@ud.demo.ru',
  pending: 'owner1@pending.demo.ru',
}

export const authHandlers = [
  http.post(api('/auth/login'), async ({ request }) => handleBodyErrors(async () => {
    await latency()
    const body = (await readJson(request)) as LoginRequest
    const user = db.data.users.find((u) => u.email.toLowerCase() === body.email?.trim().toLowerCase())
    if (!user || user.password !== body.password) {
      return problem(401, 'Неверный email или пароль', 'Проверьте данные и попробуйте ещё раз.')
    }
    return HttpResponse.json({ accessToken: tokenFor(user.id), user: toCurrentUser(user, false) })
  })),

  http.post(api('/auth/register'), async ({ request }) => handleBodyErrors(async () => {
    await latency()
    const body = (await readJson(request)) as RegisterRequest
    const email = body.user?.email?.trim().toLowerCase()
    if (!email || !body.user.password || !body.organization?.name) {
      return problem(400, 'Заполнены не все обязательные поля')
    }
    if (db.data.users.some((u) => u.email.toLowerCase() === email)) {
      return problem(409, 'Email уже зарегистрирован', 'Войдите в систему или используйте другой email.')
    }
    const suffix = Date.now().toString(36)
    const organizationId = `org_r_${suffix}`
    db.upsert('organizations', {
      id: organizationId,
      type: body.organizationType,
      name: body.organization.name.trim(),
      inn: body.organization.inn,
      city: body.organization.city,
      status: 'pending',
      createdAt: new Date().toISOString(),
      marketplaces: body.organization.marketplaces,
    })
    const user = {
      id: `usr_r_${suffix}`,
      email,
      password: body.user.password,
      fullName: body.user.fullName.trim(),
      phone: body.user.phone,
      isPlatformAdmin: false,
      memberships: [
        { organizationId, role: body.organizationType === 'seller' ? ('seller_owner' as const) : ('fc_owner' as const) },
      ],
    }
    db.upsert('users', user)
    return HttpResponse.json({ accessToken: tokenFor(user.id), user: toCurrentUser(user, false) }, { status: 201 })
  })),

  http.get(
    api('/auth/me'),
    withAuth(({ ctx }) => HttpResponse.json(toCurrentUser(ctx.user, ctx.isDemo)), { allowInactive: true }),
  ),

  http.get(api('/auth/demo/status'), async () => {
    await latency()
    return HttpResponse.json({ enabled: demoFlags.enabled })
  }),

  http.post(api('/auth/demo'), async ({ request }) => handleBodyErrors(async () => {
    await latency()
    if (!demoFlags.enabled) return problem(404, 'Не найдено')
    const body = (await readJson(request)) as { persona?: string }
    const email = DEMO_PERSONAS[(body.persona ?? '').trim().toLowerCase()]
    if (!email) return problem(422, 'Неизвестная персона')
    const user = db.data.users.find((u) => u.email.toLowerCase() === email)
    if (!user) return problem(404, 'Пользователь не найден')
    return HttpResponse.json({ accessToken: tokenFor(user.id, true), user: toCurrentUser(user, true) })
  })),
]
