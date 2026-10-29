import { z } from 'zod'

/** Схемы тел запросов мок-API: несоответствие → 422 через parseBody (вместо 500 на {} / пустом теле) */
const str = z.string({ error: 'Обязательное поле' })
const bool = z.boolean({ error: 'Ожидается true/false' })
const ids = z.array(str, { error: 'Ожидается список идентификаторов' })

export const orgProfileSchema = z.looseObject({
  name: str,
  bik: str,
  account: str,
  corrAccount: str,
  contactEmail: str,
})
export const cardRulesSchema = z.object({
  requireBarcode: bool,
  requireDimensions: bool,
  requireWeight: bool,
  requirePhoto: bool,
  requireExpiry: bool,
  requireMarking: bool,
  autoApprove: bool,
})
export const notificationPrefsSchema = z.object({
  email: bool,
  inboundStatus: bool,
  lowStock: bool,
  slaRisk: bool,
  billing: bool,
})
export const inviteSchema = z.looseObject({ email: str, role: str })
export const orderIdsSchema = z.looseObject({ orderIds: ids })
export const routeSchema = z.looseObject({ orderIds: ids, algorithm: str })
export const forecastApproveSchema = z.looseObject({ warehouseId: str.nullish(), algorithm: str })
export const applyIdsSchema = z.looseObject({ ids })
const productShape = { sku: str, name: str }
export const productSchema = z.looseObject(productShape)
export const productImportSchema = z.looseObject({ rows: z.array(z.looseObject({}), { error: 'Ожидается список строк' }) })
export const fboInputSchema = z.looseObject({
  marketplace: str,
  destination: str,
  warehouseId: str,
  lines: z.array(z.looseObject({ productId: str, quantity: z.number({ error: 'Ожидается число' }) }), {
    error: 'Ожидается список позиций',
  }),
})
export const statusToSchema = z.looseObject({ to: str })
export const inspectSchema = z.looseObject({
  grades: z.record(z.string(), str, { error: 'Ожидается объект оценок' }),
  comment: z.string().optional(),
})
export const referenceSchema = z.looseObject({ kind: str, code: str, name: str, note: z.string().optional() })
