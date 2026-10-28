import { describe, expect, it } from 'vitest'
import { countBy, listParams, paginate, sortItems } from '@/mocks/lib/http'
import { queryClient } from '@/app/query'
import { ApiError } from '@/shared/api/http'

describe('mocks/lib/http', () => {
  it('listParams: значения по умолчанию и границы', () => {
    const p = listParams(new URL('http://x/?page=-3&pageSize=9999&search=%20ABC%20'))
    expect(p).toMatchObject({ page: 1, pageSize: 200, search: 'abc', sortOrder: 'desc' })
    expect(listParams(new URL('http://x/?pageSize=abc')).pageSize).toBe(25)
    expect(listParams(new URL('http://x/'), { sortField: 'code', sortOrder: 'asc' })).toMatchObject({ sortField: 'code', sortOrder: 'asc' })
  })

  it('paginate', () => {
    const items = Array.from({ length: 12 }, (_, i) => i)
    expect(paginate(items, 3, 5)).toEqual({ items: [10, 11], total: 12, page: 3, pageSize: 5 })
    expect(paginate(items, 9, 5).items).toEqual([])
  })

  it('sortItems: числа, строки ru, null в конце, без мутации', () => {
    const items = [{ v: 2, s: 'б' }, { v: null, s: 'а' }, { v: 10, s: 'в' }]
    expect(sortItems(items, 'v', 'asc').map((i) => i.v)).toEqual([2, 10, null])
    expect(sortItems(items, 'v', 'desc').map((i) => i.v)).toEqual([10, 2, null])
    expect(sortItems(items, 's', 'asc').map((i) => i.s)).toEqual(['а', 'б', 'в'])
    expect(sortItems(items, undefined, 'asc')).toBe(items)
    expect(items[0]!.v).toBe(2)
  })

  it('countBy', () => {
    expect(countBy(['a', 'b', 'a'], (x) => x)).toEqual({ a: 2, b: 1 })
  })
})

describe('политика повторов vue-query', () => {
  const retry = queryClient.getDefaultOptions().queries!.retry as (n: number, e: unknown) => boolean
  it('4xx не повторяем, 5xx/сеть — один повтор', () => {
    expect(retry(0, new ApiError(404, { title: 'x', status: 404 } as never))).toBe(false)
    expect(retry(0, new ApiError(403, { title: 'x', status: 403 } as never))).toBe(false)
    expect(retry(0, new ApiError(500, { title: 'x', status: 500 } as never))).toBe(true)
    expect(retry(1, new ApiError(500, { title: 'x', status: 500 } as never))).toBe(false)
    expect(retry(0, new TypeError('Failed to fetch'))).toBe(true)
  })
})
