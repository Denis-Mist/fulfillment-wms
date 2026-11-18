import { healthReal, realApi } from './real-api'
import { IS_PROD, REAL_API_URL } from './env'

/**
 * Проверка стенда: жив реальный API (нужен для сценариев «выход из демо → реальный логин/регистрация»).
 * В v2 НЕ проверяем `GET /api/auth/demo/status` и `Demo__Enabled`: демо от сервера не зависит (SPEC-v2 §3.6),
 * серверный статус/сброс не критерий приёмки (§7, §11.4).
 */
export default async function globalSetup() {
  if (process.env.E2E_DEMO_SKIP_PRECHECK === '1') return
  const api = await realApi()
  const status = await healthReal(api)
  await api.dispose()
  if (status !== 200) {
    throw new Error(
      `Реальный API недоступен: ${REAL_API_URL.replace(/\/api$/, '')}/health → ${status || 'нет соединения'}. ` +
        (IS_PROD ? 'Прод-смок прерван.' : 'Поднимите API стенда тестов (см. e2e-demo/README.md); Demo__Enabled не нужен.'),
    )
  }
}
