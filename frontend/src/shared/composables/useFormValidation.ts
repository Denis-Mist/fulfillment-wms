import { computed, reactive, ref, watch, type MaybeRefOrGetter, toValue } from 'vue'
import type { z } from 'zod'
import { ApiError } from '@/shared/api/http'

/**
 * Клиентская валидация формы по zod-схеме: ошибки по полям (первая на поле), показ после blur
 * или попытки отправки, серверные ошибки (400/409) привязываются к полю через setServerError.
 */
export function useFormValidation<S extends z.ZodType>(schema: S, form: MaybeRefOrGetter<unknown>) {
  const touched = reactive<Record<string, boolean>>({})
  const server = ref<Record<string, string>>({})

  const clientErrors = computed<Record<string, string>>(() => {
    const result = schema.safeParse(toValue(form))
    if (result.success) return {}
    const out: Record<string, string> = {}
    for (const issue of result.error.issues) {
      const key = String(issue.path[0] ?? '')
      out[key] ??= issue.message
    }
    return out
  })
  const errors = computed<Record<string, string | undefined>>(() => ({
    ...server.value,
    ...clientErrors.value,
  }))

  // Изменение поля снимает серверную ошибку с него
  watch(
    () => JSON.stringify(toValue(form)),
    () => (server.value = {}),
  )

  const show = (key: string) => (touched[key] || server.value[key] ? errors.value[key] : undefined)

  /** true, если форма валидна; иначе помечает все поля как тронутые */
  function validate() {
    for (const key of Object.keys(clientErrors.value)) touched[key] = true
    return Object.keys(clientErrors.value).length === 0
  }

  /** Привязать ошибку API к полю; возвращает true, если ошибка обработана */
  function setServerError(error: unknown, field: string, statuses = [400, 409]) {
    if (!(error instanceof ApiError) || !statuses.includes(error.status)) return false
    server.value = { [field]: error.problem.detail ?? error.problem.title }
    return true
  }

  function reset() {
    for (const key of Object.keys(touched)) delete touched[key]
    server.value = {}
  }

  return { errors, touched, show, validate, setServerError, reset }
}
