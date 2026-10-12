import i18n from '@/i18n'

/** Clinic-level formatting context, set once the user's clinic is known. */
const ctx = { currency: 'JOD', timeZone: 'Asia/Amman' }

export function setFormatContext(currency: string, timeZone: string) {
  ctx.currency = currency
  ctx.timeZone = timeZone
}

export const clinicTimeZone = () => ctx.timeZone

const locale = () => (i18n.language === 'ar' ? 'ar-JO-u-nu-latn' : 'en-GB')

const currencyLabel = () => (ctx.currency === 'JOD' && i18n.language === 'ar' ? 'د.أ' : ctx.currency)

/** 450 → "450 JOD"; JOD uses up to 3 decimals (fils). */
export function money(value: number | null | undefined, opts: { compact?: boolean } = {}) {
  const v = value ?? 0
  const num = new Intl.NumberFormat(locale(), {
    maximumFractionDigits: opts.compact ? 1 : 2,
    notation: opts.compact && Math.abs(v) >= 10_000 ? 'compact' : 'standard',
  }).format(v)
  return `${num} ${currencyLabel()}`
}

export const number = (v: number | null | undefined, digits = 0) =>
  new Intl.NumberFormat(locale(), { maximumFractionDigits: digits }).format(v ?? 0)

export const percent = (v: number | null | undefined) => `${number(v, 1)}%`

const fmt = (iso: string | Date | null | undefined, options: Intl.DateTimeFormatOptions) => {
  if (!iso) return '—'
  const d = typeof iso === 'string' ? new Date(iso) : iso
  if (Number.isNaN(d.getTime())) return '—'
  return new Intl.DateTimeFormat(locale(), { timeZone: ctx.timeZone, ...options }).format(d)
}

export const date = (iso?: string | Date | null) => fmt(iso, { day: 'numeric', month: 'short', year: 'numeric' })
export const shortDate = (iso?: string | Date | null) => fmt(iso, { day: 'numeric', month: 'short' })
export const time = (iso?: string | Date | null) => fmt(iso, { hour: 'numeric', minute: '2-digit' })
export const dateTime = (iso?: string | Date | null) => fmt(iso, { day: 'numeric', month: 'short', hour: 'numeric', minute: '2-digit' })
export const weekday = (iso?: string | Date | null) => fmt(iso, { weekday: 'long', day: 'numeric', month: 'long' })
export const monthLabel = (yyyyMm: string) => fmt(new Date(`${yyyyMm}-15T12:00:00Z`), { month: 'short', year: '2-digit' })

/** "3 days ago", "in 2 hours" in the active language. */
export function relative(iso?: string | null) {
  if (!iso) return '—'
  const diffMs = new Date(iso).getTime() - Date.now()
  const rtf = new Intl.RelativeTimeFormat(i18n.language === 'ar' ? 'ar' : 'en', { numeric: 'auto' })
  const abs = Math.abs(diffMs)
  const minute = 60_000, hour = 60 * minute, day = 24 * hour
  if (abs < hour) return rtf.format(Math.round(diffMs / minute), 'minute')
  if (abs < day) return rtf.format(Math.round(diffMs / hour), 'hour')
  if (abs < 30 * day) return rtf.format(Math.round(diffMs / day), 'day')
  if (abs < 365 * day) return rtf.format(Math.round(diffMs / (30 * day)), 'month')
  return rtf.format(Math.round(diffMs / (365 * day)), 'year')
}

/** YYYY-MM-DD for a Date in the clinic time zone. */
export function isoDateInClinic(d: Date = new Date()) {
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone: ctx.timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).format(d)
  return parts
}

/** Converts a clinic-local "YYYY-MM-DDTHH:mm" to a UTC ISO string (handles the Amman offset correctly). */
export function clinicLocalToUtc(local: string): string {
  const [datePart, timePart = '00:00'] = local.split('T')
  const [y, m, d] = datePart.split('-').map(Number)
  const [hh, mm] = timePart.split(':').map(Number)
  const guess = Date.UTC(y, m - 1, d, hh, mm)
  const offset = tzOffsetMinutes(new Date(guess))
  return new Date(guess - offset * 60_000).toISOString()
}

/** UTC ISO → clinic-local "YYYY-MM-DDTHH:mm" for <input type="datetime-local">. */
export function utcToClinicLocal(iso: string): string {
  const d = new Date(iso)
  const p = Object.fromEntries(
    new Intl.DateTimeFormat('en-GB', { timeZone: ctx.timeZone, year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' })
      .formatToParts(d).map((x) => [x.type, x.value]),
  )
  return `${p.year}-${p.month}-${p.day}T${p.hour}:${p.minute}`
}

function tzOffsetMinutes(at: Date) {
  const p = Object.fromEntries(
    new Intl.DateTimeFormat('en-GB', { timeZone: ctx.timeZone, year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23' })
      .formatToParts(at).map((x) => [x.type, x.value]),
  )
  const asUtc = Date.UTC(+p.year, +p.month - 1, +p.day, +p.hour, +p.minute, +p.second)
  return Math.round((asUtc - at.getTime()) / 60_000)
}

export const bytes = (n: number) =>
  n < 1024 ? `${n} B` : n < 1024 * 1024 ? `${(n / 1024).toFixed(0)} KB` : `${(n / 1024 / 1024).toFixed(1)} MB`

export const initials = (name: string) =>
  name.replace(/^(Dr\.?|د\.)\s*/i, '').split(/\s+/).filter(Boolean).slice(0, 2).map((s) => s[0]).join('').toUpperCase()
