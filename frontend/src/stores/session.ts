import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { useStorage } from '@vueuse/core'
import { authApi } from '@/modules/auth/api'
import { cabinetForRole } from '@/modules/auth/roles'
import type { AuthResponse, Cabinet, CurrentUser, LoginRequest, RegisterRequest, Role } from '@/modules/auth/types'
import { queryClient } from '@/app/query'

export const useSessionStore = defineStore('session', () => {
  const token = useStorage<string>('wms.token', '')
  const organizationId = useStorage<string>('wms.organizationId', '')
  /** Пустая строка — «Все склады» */
  const warehouseId = useStorage<string>('wms.warehouseId', '')
  const user = ref<CurrentUser | null>(null)
  let initPromise: Promise<void> | null = null

  const isAuthenticated = computed(() => !!token.value && !!user.value)

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
    user.value = next
    if (!next.memberships.some((m) => m.organizationId === organizationId.value)) {
      organizationId.value = next.memberships[0]?.organizationId ?? ''
      warehouseId.value = ''
    }
  }

  function applyAuth(response: AuthResponse) {
    token.value = response.accessToken
    queryClient.clear()
    setUser(response.user)
  }

  function clear() {
    token.value = ''
    user.value = null
    organizationId.value = ''
    warehouseId.value = ''
    queryClient.clear()
  }

  function init() {
    initPromise ??= (async () => {
      if (!token.value) return
      try {
        setUser(await authApi.me())
      } catch {
        clear()
      }
    })()
    return initPromise
  }

  async function login(request: LoginRequest) {
    applyAuth(await authApi.login(request))
  }

  async function register(request: RegisterRequest) {
    applyAuth(await authApi.register(request))
  }

  async function refreshUser() {
    setUser(await authApi.me())
  }

  function logout() {
    clear()
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
    activeMembership,
    role,
    cabinet,
    isRestricted,
    init,
    login,
    register,
    refreshUser,
    logout,
    setOrganization,
    setWarehouse,
  }
})
