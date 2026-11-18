import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { useStorage } from '@vueuse/core'
import { authApi, type DemoPersona } from '@/modules/auth/api'
import { cabinetForRole } from '@/modules/auth/roles'
import type {
  AuthResponse,
  Cabinet,
  CurrentUser,
  LoginRequest,
  RegisterRequest,
  Role,
} from '@/modules/auth/types'
import { queryClient } from '@/app/query'
import {
  DEMO_SESSION_KEYS,
  REAL_SESSION_KEYS,
  enterDemoTab,
  isAlwaysMocks,
  isDemoTab,
  leaveDemoTab,
  readDemoPersona,
  writeDemoPersona,
} from '@/app/demoMode'
import { ApiError } from '@/shared/api/http'
import { tokenLooksDemo } from '@/modules/auth/token'

export type LogoutDestination = 'login' | 'home'

export const useSessionStore = defineStore('session', () => {
  const realToken = useStorage<string>(REAL_SESSION_KEYS.token, '')
  const realOrganizationId = useStorage<string>(REAL_SESSION_KEYS.organizationId, '')
  const realWarehouseId = useStorage<string>(REAL_SESSION_KEYS.warehouseId, '')
  const demoToken = useStorage<string>(DEMO_SESSION_KEYS.token, '')
  const demoOrganizationId = useStorage<string>(DEMO_SESSION_KEYS.organizationId, '')
  const demoWarehouseId = useStorage<string>(DEMO_SESSION_KEYS.warehouseId, '')

  /** Режим этой вкладки; не подписываемся на storage-события, чтобы боевая вкладка не переключилась. */
  const demoTab = ref(isDemoTab())

  const user = ref<CurrentUser | null>(null)
  let initPromise: Promise<void> | null = null
  /** Ошибка восстановления сессии (5xx/сеть): токен есть, пользователя нет — App покажет экран «Повторить» (FE-S2-1) */
  const initError = ref<unknown>(null)
  /** Сломанная/пропавшая мок-сессия: гвард ведёт на /demo, не на /login */
  const demoExpired = ref(false)
  const initializing = ref(false)

  const token = computed({
    get: () => (demoTab.value ? demoToken.value : realToken.value),
    set: (value) => {
      if (demoTab.value) demoToken.value = value
      else realToken.value = value
    },
  })
  const organizationId = computed({
    get: () => (demoTab.value ? demoOrganizationId.value : realOrganizationId.value),
    set: (value) => {
      if (demoTab.value) demoOrganizationId.value = value
      else realOrganizationId.value = value
    },
  })
  const warehouseId = computed({
    get: () => (demoTab.value ? demoWarehouseId.value : realWarehouseId.value),
    set: (value) => {
      if (demoTab.value) demoWarehouseId.value = value
      else realWarehouseId.value = value
    },
  })

  const isAuthenticated = computed(() => !!token.value && !!user.value)
  const isDemo = computed(() => demoTab.value && !!user.value?.isDemo)

  const activeMembership = computed(() => {
    if (!user.value || user.value.isPlatformAdmin) return null
    return (
      user.value.memberships.find((m) => m.organizationId === organizationId.value) ??
      user.value.memberships[0] ??
      null
    )
  })

  const role = computed<Role | null>(() => {
    if (user.value?.isPlatformAdmin) return 'platform_admin'
    return activeMembership.value?.role ?? null
  })

  const cabinet = computed<Cabinet | null>(() => (role.value ? cabinetForRole(role.value) : null))

  /** Организация ещё не прошла модерацию или заблокирована */
  const isRestricted = computed(
    () => !!activeMembership.value && activeMembership.value.organizationStatus !== 'active',
  )

  function setUser(next: CurrentUser) {
    user.value = { ...next, isDemo: Boolean(next.isDemo) }
    if (!next.memberships.some((m) => m.organizationId === organizationId.value)) {
      organizationId.value = next.memberships[0]?.organizationId ?? ''
      warehouseId.value = ''
    }
  }

  function applyAuth(response: AuthResponse) {
    demoExpired.value = false
    token.value = response.accessToken
    queryClient.clear()
    setUser(response.user)
  }

  function clearActiveKeys() {
    initError.value = null
    token.value = ''
    user.value = null
    organizationId.value = ''
    warehouseId.value = ''
    queryClient.clear()
  }

  function clearDemoKeys() {
    demoToken.value = ''
    demoOrganizationId.value = ''
    demoWarehouseId.value = ''
    writeDemoPersona(null)
  }

  function resetInit() {
    initPromise = null
  }

  function init() {
    initPromise ??= (async () => {
      initializing.value = true
      try {
        if (!token.value) return
        // Разлогиниваем только при явном отказе (401/403); 404/5xx/сеть — пара быстрых повторов, затем экран ошибки
        let lastError: unknown = null
        for (let attempt = 0; attempt < 2; attempt++) {
          try {
            setUser(await authApi.me())
            initError.value = null
            return
          } catch (error) {
            if (error instanceof ApiError && (error.status === 401 || error.status === 403)) {
              handleUnauthorized()
              initError.value = null
              return
            }
            lastError = error
            if (attempt === 0) await new Promise((r) => setTimeout(r, 400))
          }
        }
        initError.value = lastError ?? new Error('Не удалось загрузить профиль')
      } finally {
        initializing.value = false
      }
    })()
    return initPromise
  }

  /** «Повторить» на экране ошибки: заново запрашиваем /auth/me без перезагрузки */
  async function retryInit() {
    resetInit()
    await init()
  }

  async function login(request: LoginRequest) {
    applyAuth(await authApi.login(request))
  }

  async function loginDemo(persona: DemoPersona) {
    const { startMockWorker } = await import('@/mocks/runtime')
    await startMockWorker()
    enterDemoTab()
    demoTab.value = true
    resetInit()
    writeDemoPersona(persona)
    applyAuth(await authApi.demo(persona))
  }

  async function register(request: RegisterRequest) {
    applyAuth(await authApi.register(request))
  }

  async function refreshUser() {
    setUser(await authApi.me())
  }

  async function resetDemo() {
    const { resetMockData } = await import('@/mocks/runtime')
    const persona = (readDemoPersona() ?? undefined) as DemoPersona | undefined
    resetMockData()
    queryClient.clear()
    if (persona && demoTab.value) {
      applyAuth(await authApi.demo(persona))
      return 'home' as const
    }
    return 'demo' as const
  }

  function handleUnauthorized() {
    if (demoTab.value || tokenLooksDemo(token.value)) {
      demoExpired.value = true
      clearActiveKeys()
      return
    }
    clearActiveKeys()
  }

  async function exitDemo(): Promise<LogoutDestination> {
    clearDemoKeys()
    user.value = null
    initError.value = null
    queryClient.clear()
    demoExpired.value = false
    leaveDemoTab()
    demoTab.value = false
    resetInit()
    if (!isAlwaysMocks()) {
      const { stopMockWorker } = await import('@/mocks/runtime')
      await stopMockWorker()
    }
    if (realToken.value) {
      await init()
      if (isAuthenticated.value) return 'home'
    }
    return 'login'
  }

  async function logout(): Promise<LogoutDestination> {
    if (demoTab.value) return exitDemo()
    demoExpired.value = false
    clearActiveKeys()
    return 'login'
  }

  function setOrganization(id: string) {
    organizationId.value = id
    warehouseId.value = ''
    queryClient.clear()
  }

  function setWarehouse(id: string) {
    warehouseId.value = id
  }

  return {
    token,
    user,
    organizationId,
    warehouseId,
    isAuthenticated,
    isDemo,
    demoTab,
    demoExpired,
    initializing,
    handleUnauthorized,
    activeMembership,
    role,
    cabinet,
    isRestricted,
    init,
    initError,
    retryInit,
    login,
    loginDemo,
    register,
    refreshUser,
    logout,
    exitDemo,
    resetDemo,
    setOrganization,
    setWarehouse,
    realToken,
  }
})
