/** Детерминированный PRNG (mulberry32): одинаковые демо-данные при каждом запуске */
export function createRandom(seed: number) {
  let state = seed >>> 0

  const next = () => {
    state = (state + 0x6d2b79f5) | 0
    let t = Math.imul(state ^ (state >>> 15), 1 | state)
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }

  const int = (min: number, max: number) => Math.floor(next() * (max - min + 1)) + min

  return {
    next,
    int,
    float: (min: number, max: number) => next() * (max - min) + min,
    bool: (probability = 0.5) => next() < probability,
    pick: <T>(items: readonly T[]): T => items[Math.floor(next() * items.length)]!,
    weighted: <T>(items: readonly (readonly [T, number])[]): T => {
      const total = items.reduce((sum, [, weight]) => sum + weight, 0)
      let roll = next() * total
      for (const [value, weight] of items) {
        roll -= weight
        if (roll <= 0) return value
      }
      return items[items.length - 1]![0]
    },
    sample: <T>(items: readonly T[], count: number): T[] => {
      const copy = [...items]
      for (let i = copy.length - 1; i > 0; i--) {
        const j = Math.floor(next() * (i + 1))
        ;[copy[i], copy[j]] = [copy[j]!, copy[i]!]
      }
      return copy.slice(0, Math.min(count, copy.length))
    },
    digits: (length: number) => Array.from({ length }, () => int(0, 9)).join(''),
  }
}

export type Random = ReturnType<typeof createRandom>
