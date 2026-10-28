/**
 * Дополнительная коллекция моков с сохранением изменений в localStorage.
 * Базовые записи генерируются детерминированно, правки пользователя накладываются поверх.
 */
export function localCollection<T extends { id: string }>(key: string, seed: () => T[]) {
  const storageKey = `wms.mock.${key}.v1`
  let items: T[] | null = null

  const load = (): T[] => {
    if (items) return items
    items = seed()
    try {
      const raw = localStorage.getItem(storageKey)
      const overlay = raw ? (JSON.parse(raw) as Record<string, T>) : {}
      for (const record of Object.values(overlay)) {
        const index = items.findIndex((item) => item.id === record.id)
        if (index >= 0) items[index] = record
        else items.push(record)
      }
    } catch {
      /* повреждённый overlay игнорируем */
    }
    return items
  }

  return {
    get all(): T[] {
      return load()
    },
    find: (id: string) => load().find((item) => item.id === id),
    upsert(record: T) {
      const list = load()
      const index = list.findIndex((item) => item.id === record.id)
      if (index >= 0) list[index] = record
      else list.push(record)
      let overlay: Record<string, T> = {}
      try {
        overlay = JSON.parse(localStorage.getItem(storageKey) ?? '{}') as Record<string, T>
      } catch {
        overlay = {}
      }
      overlay[record.id] = record
      localStorage.setItem(storageKey, JSON.stringify(overlay))
      return record
    },
  }
}
