export type ForecastAlgorithm = 'holt_winters' | 'moving_average'
export type SlottingAlgorithm = 'abc_affinity' | 'abc'
export type AbcClass = 'A' | 'B' | 'C'

export interface CalcMeta {
  /** Версия алгоритма и зерно — для воспроизводимости расчёта (NFR-09) */
  algorithm: string
  version: string
  seed: number
  dataVersion: string
  computedAt: string
  durationMs: number
}

export interface ForecastPoint {
  date: string
  actual?: number
  predicted?: number
  low?: number
  high?: number
}

export interface StaffingDay {
  date: string
  orders: number
  units: number
  pickers: number
  packers: number
  receivers: number
  total: number
  available: number
}

export interface ForecastResult {
  meta: CalcMeta
  horizon: number
  series: ForecastPoint[]
  backtest: { mape: number; mae: number; baselineMape: number; days: number }
  staffing: StaffingDay[]
  plan?: { approvedAt: string; approvedBy: string; algorithm: ForecastAlgorithm }
}

export interface SlottingMove {
  id: string
  productId: string
  sku: string
  name: string
  sellerName: string
  abc: AbcClass
  picks30d: number
  affinitySku?: string
  fromCell: string
  fromDistance: number
  toCell: string
  toDistance: number
  /** Ожидаемое сокращение пробега за 30 дней, м */
  gain: number
  applied: boolean
}

export interface SlottingResult {
  meta: CalcMeta
  classes: Record<AbcClass, { sku: number; picksShare: number }>
  currentDistance: number
  projectedDistance: number
  reductionPct: number
  moves: SlottingMove[]
}

export interface KpiPeriodPoint {
  date: string
  orders: number
  processingHours: number
  routeBaseline: number
  routeOptimized: number
  utilization: number
  errors: number
}

export interface KpiReport {
  period: number
  totals: {
    orders: number
    throughputPerDay: number
    avgProcessingHours: number
    avgRouteBaseline: number
    avgRouteOptimized: number
    routeReductionPct: number
    utilization: number
    errorRate: number
  }
  series: KpiPeriodPoint[]
  bySeller: {
    sellerId: string
    name: string
    orders: number
    avgProcessingHours: number
    errorRate: number
  }[]
}
