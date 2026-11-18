import { db } from './db/store'
import { resetLocalCollections } from './lib/collection'

/** Очищает overlay и восстанавливает in-memory seed (без перезагрузки страницы). */
export function resetMockData() {
  db.reset()
  resetLocalCollections()
}
