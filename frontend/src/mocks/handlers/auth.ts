import { http, HttpResponse } from 'msw'
import type { LoginRequest, RegisterRequest } from '@/modules/auth/types'
import { db } from '../db/store'
import { handleBodyErrors, readJson, latency, problem, tokenFor, withAuth } from '../lib/http'
import { toCurrentUser } from '../lib/mappers'
import { api } from './api'

export const authHandlers = [
  http.post(api('/auth/login'), async ({ request }) => handleBodyErrors(async () => {
    await latency()
    const body = (await readJson(request)) as LoginRequest
    const user = db.data.users.find((u) => u.email.toLowerCase() === body.email?.trim().toLowerCase())
    if (!user || user.password !== body.password) {
      return problem(401, 'Неверный email или пароль', 'Проверьте данные и попробуйте ещё раз.')
    }
    return HttpResponse.json({ accessToken: tokenFor(user.id), user: toCurrentUser(user) })
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
    return HttpResponse.json({ accessToken: tokenFor(user.id), user: toCurrentUser(user) }, { status: 201 })
  })),

  http.get(
    api('/auth/me'),
    withAuth(({ ctx }) => HttpResponse.json(toCurrentUser(ctx.user)), { allowInactive: true }),
  ),
]
