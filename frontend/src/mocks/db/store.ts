import { seed } from './seed'
import type { CollectionName, Collections, RecordOf } from './types'

const STORAGE_KEY = 'wms.mockdb.v1'

type Overlay = {
  [K in CollectionName]?: { upserts: Record<string, RecordOf<K>>; deleted: string[] }
}

/**
 * In-memory БД моков. Базовые данные генерируются детерминированно,
 * изменения пользователя хранятся отдельно в localStorage и накладываются поверх.
 */
class MockDb {
  readonly data: Collections
  private overlay: Overlay

  constructor() {
    this.data = seed()
    this.overlay = this.load()
    this.applyOverlay()
  }

  upsert<K extends CollectionName>(name: K, record: RecordOf<K>) {
    const list = this.data[name] as RecordOf<K>[]
    const index = list.findIndex((item) => item.id === record.id)
    if (index >= 0) list[index] = record
    else list.push(record)
    const entry = this.entry(name)
    entry.upserts[record.id] = record
    entry.deleted = entry.deleted.filter((id) => id !== record.id)
    this.save()
  }

  remove<K extends CollectionName>(name: K, id: string) {
    const list = this.data[name] as RecordOf<K>[]
    const index = list.findIndex((item) => item.id === id)
    if (index >= 0) list.splice(index, 1)
    const entry = this.entry(name)
    delete entry.upserts[id]
    if (!entry.deleted.includes(id)) entry.deleted.push(id)
    this.save()
  }

  reset() {
    localStorage.removeItem(STORAGE_KEY)
  }

  private entry<K extends CollectionName>(name: K) {
    const existing = this.overlay[name]
    if (existing) return existing as { upserts: Record<string, RecordOf<K>>; deleted: string[] }
    const created = { upserts: {} as Record<string, RecordOf<K>>, deleted: [] as string[] }
    ;(this.overlay as Record<string, unknown>)[name] = created
    return created
  }

  private applyOverlay() {
    for (const name of Object.keys(this.overlay) as CollectionName[]) {
      const entry = this.overlay[name]
      if (!entry) continue
      const list = this.data[name] as { id: string }[]
      for (const id of entry.deleted) {
        const index = list.findIndex((item) => item.id === id)
        if (index >= 0) list.splice(index, 1)
      }
      for (const record of Object.values(entry.upserts) as { id: string }[]) {
        const index = list.findIndex((item) => item.id === record.id)
        if (index >= 0) list[index] = record
        else list.push(record)
      }
    }
  }

  private load(): Overlay {
    try {
      const raw = localStorage.getItem(STORAGE_KEY)
      return raw ? (JSON.parse(raw) as Overlay) : {}
    } catch {
      return {}
    }
  }

  private save() {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(this.overlay))
  }
}

export const db = new MockDb()
