import { createRouter, createWebHistory, type RouteRecordRaw, type RouteRecordSingleView } from 'vue-router'
import { CABINET_HOME } from '@/modules/auth/roles'
import type { Cabinet, Role } from '@/modules/auth/types'
import { useSessionStore } from '@/stores/session'
import { CABINETS, routeName, type CabinetDef } from './sections'

declare module 'vue-router' {
  interface RouteMeta {
    title?: string
    description?: string
    cabinet?: Cabinet
    roles?: Role[]
    public?: boolean
    guestOnly?: boolean
    planned?: string[]
    /** Имя маршрута пункта меню, который нужно подсветить */
    navName?: string
  }
}

const AuthLayout = () => import('@/layouts/AuthLayout.vue')
const CabinetLayout = () => import('@/layouts/CabinetLayout.vue')
const WorkerLayout = () => import('@/layouts/WorkerLayout.vue')
const PlaceholderPage = () => import('@/pages/common/PlaceholderPage.vue')

function cabinetRoutes(def: CabinetDef): RouteRecordRaw {
  return {
    path: def.basePath,
    component: def.id === 'worker' ? WorkerLayout : CabinetLayout,
    meta: { cabinet: def.id },
    children: def.groups
      .flatMap((group) => group.sections)
      .map((section) => ({
        path: section.path,
        name: routeName(def.id, section.name),
        component: section.page ?? PlaceholderPage,
        meta: {
          title: section.title,
          description: section.description,
          cabinet: def.id,
          roles: section.roles,
          planned: section.planned,
          navName: routeName(def.id, section.navParent ?? section.name),
        },
      })),
  }
}

const authPage = (
  path: string,
  name: string,
  title: string,
  page: RouteRecordSingleView['component'],
  meta = {},
): RouteRecordRaw => ({
  path,
  component: AuthLayout,
  children: [{ path: '', name, component: page, meta: { title, ...meta } }],
})

const routes: RouteRecordRaw[] = [
  { path: '/', name: 'home', component: { render: () => null } },
  authPage('/login', 'login', 'Вход', () => import('@/pages/auth/LoginPage.vue'), { public: true, guestOnly: true }),
  authPage('/register', 'register', 'Регистрация', () => import('@/pages/auth/RegisterPage.vue'), {
    public: true,
    guestOnly: true,
  }),
  authPage('/pending', 'pending', 'Проверка заявки', () => import('@/pages/auth/PendingPage.vue')),
  ...CABINETS.map(cabinetRoutes),
  {
    path: '/:pathMatch(.*)*',
    name: 'not-found',
    component: () => import('@/pages/common/NotFoundPage.vue'),
    meta: { title: 'Страница не найдена', public: true },
  },
]

export const router = createRouter({
  history: createWebHistory(),
  routes,
  scrollBehavior: (_to, _from, saved) => saved ?? { top: 0 },
})

router.beforeEach(async (to) => {
  const session = useSessionStore()
  await session.init()
  const home = () => ({ name: CABINET_HOME[session.cabinet ?? 'fc'] })

  if (to.meta.public) {
    if (to.meta.guestOnly && session.isAuthenticated) return home()
    return true
  }
  if (!session.isAuthenticated) {
    return { name: 'login', query: to.fullPath !== '/' ? { redirect: to.fullPath } : undefined }
  }
  if (session.isRestricted) return to.name === 'pending' ? true : { name: 'pending' }
  if (to.name === 'pending' || to.name === 'home') return home()
  if (to.meta.cabinet && to.meta.cabinet !== session.cabinet) return home()
  if (to.meta.roles?.length && session.role && !to.meta.roles.includes(session.role)) return home()
  return true
})

router.afterEach((to) => {
  document.title = to.meta.title ? `${to.meta.title} · Fulfillment WMS` : 'Fulfillment WMS'
})
