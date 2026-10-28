import { describe, expect, it } from 'vitest'
import { nextTick, reactive } from 'vue'
import { z } from 'zod'
import { useFormValidation } from '@/shared/composables/useFormValidation'
import { ApiError } from '@/shared/api/http'

const schema = z.object({
  code: z.string().regex(/^[A-Z0-9]{2,6}$/, 'Код: 2–6 латинских букв или цифр'),
  name: z.string().trim().min(1, 'Укажите название'),
})

describe('useFormValidation', () => {
  it('ошибки показываются только после validate/blur', () => {
    const form = reactive({ code: '', name: '' })
    const v = useFormValidation(schema, form)
    expect(v.show('code')).toBeUndefined()
    expect(v.validate()).toBe(false)
    expect(v.show('code')).toBe('Код: 2–6 латинских букв или цифр')
    expect(v.show('name')).toBe('Укажите название')
  })

  it('валидная форма', () => {
    const v = useFormValidation(schema, reactive({ code: 'MZ1', name: 'Зона' }))
    expect(v.validate()).toBe(true)
    expect(v.errors.value).toEqual({})
  })

  it('серверная 409 привязывается к полю и снимается при изменении', async () => {
    const form = reactive({ code: 'MZ1', name: 'Зона' })
    const v = useFormValidation(schema, form)
    expect(v.setServerError(new ApiError(409, { title: 'Зона с таким кодом уже существует', status: 409 } as never), 'code')).toBe(true)
    expect(v.show('code')).toBe('Зона с таким кодом уже существует')
    form.code = 'MZ2'
    await nextTick()
    expect(v.show('code')).toBeUndefined()
    expect(v.setServerError(new ApiError(500, { title: 'x', status: 500 } as never), 'code')).toBe(false)
    expect(v.setServerError(new Error('x'), 'code')).toBe(false)
  })

  it('reset снимает тронутые поля', () => {
    const v = useFormValidation(schema, reactive({ code: '', name: '' }))
    v.validate()
    v.reset()
    expect(v.show('code')).toBeUndefined()
  })
})
