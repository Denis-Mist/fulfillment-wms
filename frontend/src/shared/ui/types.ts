export interface ChipOption<V extends string> {
  value: V | undefined
  label: string
  count?: number
  danger?: boolean
}

export interface Step {
  key: string
  label: string
  at?: string
}

export interface InfoItem {
  label: string
  value?: string | number | null
}
