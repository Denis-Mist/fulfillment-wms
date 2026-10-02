import type { Component } from 'vue'
import { ScanBarcode, ShieldCheck, Store, Warehouse } from '@lucide/vue'

export interface DemoAccount {
  email: string
  title: string
  description: string
  icon: Component
}

// Email должны совпадать с пользователями генератора моков (src/mocks/db/seed.ts), пароль — `demo`
export const DEMO_ACCOUNTS: DemoAccount[] = [
  { email: 'director@demo.ru', title: 'Директор ФЦ', description: 'СкладПро · 3 склада', icon: Warehouse },
  { email: 'seller@demo.ru', title: 'Селлер', description: 'Уютный дом · 2 ФЦ', icon: Store },
  { email: 'picker@demo.ru', title: 'Комплектовщик', description: 'Режим ТСД', icon: ScanBarcode },
  { email: 'admin@demo.ru', title: 'Администратор', description: 'Вся платформа', icon: ShieldCheck },
]

export const DEMO_PASSWORD = 'demo'
