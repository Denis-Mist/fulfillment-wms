import dayjs from 'dayjs'
import 'dayjs/locale/ru'
import relativeTime from 'dayjs/plugin/relativeTime'

dayjs.extend(relativeTime)
dayjs.locale('ru')

const numberFormatters = new Map<number, Intl.NumberFormat>()

export function formatNumber(value: number, fractionDigits = 0): string {
  let formatter = numberFormatters.get(fractionDigits)
  if (!formatter) {
    formatter = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: fractionDigits })
    numberFormatters.set(fractionDigits, formatter)
  }
  return formatter.format(value)
}

const moneyFormatter = new Intl.NumberFormat('ru-RU', {
  style: 'currency',
  currency: 'RUB',
  maximumFractionDigits: 0,
})

export function formatMoney(value: number): string {
  return moneyFormatter.format(value)
}

export function formatCompactMoney(value: number): string {
  if (Math.abs(value) >= 1_000_000) return `${formatNumber(value / 1_000_000, 1)} млн ₽`
  if (Math.abs(value) >= 10_000) return `${formatNumber(value / 1_000, 0)} тыс ₽`
  return formatMoney(value)
}

export function formatPercent(value: number, fractionDigits = 0): string {
  return `${formatNumber(value, fractionDigits)}%`
}

export function formatDate(iso: string): string {
  return dayjs(iso).format('D MMM YYYY')
}

export function formatShortDate(iso: string): string {
  return dayjs(iso).format('D MMM')
}

export function formatDateTime(iso: string): string {
  const date = dayjs(iso)
  return date.year() === dayjs().year() ? date.format('D MMM, HH:mm') : date.format('D MMM YYYY, HH:mm')
}

export function formatTime(iso: string): string {
  return dayjs(iso).format('HH:mm')
}

export function formatRelative(iso: string): string {
  return dayjs(iso).fromNow()
}

/** pluralize(5, ['заказ', 'заказа', 'заказов']) → 'заказов' */
export function pluralize(count: number, forms: [string, string, string]): string {
  const n = Math.abs(count) % 100
  const n1 = n % 10
  if (n > 10 && n < 20) return forms[2]
  if (n1 > 1 && n1 < 5) return forms[1]
  if (n1 === 1) return forms[0]
  return forms[2]
}

export function greeting(date = new Date()): string {
  const hour = date.getHours()
  if (hour < 5) return 'Доброй ночи'
  if (hour < 12) return 'Доброе утро'
  if (hour < 18) return 'Добрый день'
  return 'Добрый вечер'
}

export function initials(fullName: string): string {
  return fullName
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase())
    .join('')
}
