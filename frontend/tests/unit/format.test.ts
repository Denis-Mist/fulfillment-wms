import { describe, expect, it } from 'vitest'
import {
  formatCompactMoney,
  formatMoney,
  formatNumber,
  formatPercent,
  greeting,
  initials,
  pluralize,
} from '@/shared/lib/format'

// Intl в ru-RU разделяет разряды неразрывным пробелом — нормализуем для сравнения
const plain = (s: string) => s.replace(/[\u00a0\u202f]/g, ' ')

describe('format', () => {
  it('formatNumber: разряды и дробная часть', () => {
    expect(plain(formatNumber(1768))).toBe('1 768')
    expect(plain(formatNumber(95.34, 1))).toBe('95,3')
    expect(formatNumber(0)).toBe('0')
  })

  it('formatMoney / formatCompactMoney', () => {
    expect(plain(formatMoney(1500))).toBe('1 500 ₽')
    expect(plain(formatCompactMoney(2_500_000))).toBe('2,5 млн ₽')
    expect(plain(formatCompactMoney(45_000))).toBe('45 тыс ₽')
    expect(plain(formatCompactMoney(9_999))).toBe('9 999 ₽')
    expect(plain(formatCompactMoney(-3_000_000))).toBe('-3 млн ₽')
  })

  it('formatPercent', () => {
    expect(formatPercent(12)).toBe('12%')
    expect(formatPercent(95.33, 1)).toBe('95,3%')
  })

  it.each([
    [1, 'заказ'],
    [2, 'заказа'],
    [4, 'заказа'],
    [5, 'заказов'],
    [11, 'заказов'],
    [14, 'заказов'],
    [21, 'заказ'],
    [22, 'заказа'],
    [111, 'заказов'],
    [0, 'заказов'],
  ])('pluralize(%i) → %s', (n, word) => {
    expect(pluralize(n, ['заказ', 'заказа', 'заказов'])).toBe(word)
  })

  it('greeting по часам', () => {
    const at = (h: number) => greeting(new Date(2026, 0, 1, h))
    expect(at(3)).toBe('Доброй ночи')
    expect(at(9)).toBe('Доброе утро')
    expect(at(14)).toBe('Добрый день')
    expect(at(20)).toBe('Добрый вечер')
  })

  it('initials', () => {
    expect(initials('Алексей Воронцов')).toBe('АВ')
    expect(initials('  ирина   соколова петровна ')).toBe('ИС')
    expect(initials('')).toBe('')
  })
})
