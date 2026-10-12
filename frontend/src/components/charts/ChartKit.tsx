import { useMemo, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { BarChart3, Table2 } from 'lucide-react'
import { cssVar, useThemeVersion } from '@/lib/theme'
import { cn } from '@/lib/utils'
import { Card, CardHeader, EmptyState, Skeleton } from '@/components/ui/primitives'
import { Table, THead, Td, Th, Tr } from '@/components/ui/Table'

/** Resolved chart colours for the active theme (re-read whenever light/dark changes). */
export function useChartColors() {
  const version = useThemeVersion()
  return useMemo(() => {
    void version
    return {
      series: [1, 2, 3, 4, 5].map((i) => cssVar(`--series-${i}`)),
      grid: cssVar('--chart-grid'),
      axis: cssVar('--chart-axis'),
      surface: cssVar('--surface'),
      text: cssVar('--text'),
      good: cssVar('--good'),
      critical: cssVar('--critical'),
      neutral: cssVar('--border-strong'),
      recovery: cssVar('--recovery-from'),
      primary: cssVar('--primary'),
    }
  }, [version])
}

export const axisProps = (axis: string) => ({
  tick: { fill: axis, fontSize: 11 },
  tickLine: false,
  axisLine: false,
})

export interface LegendItem { label: string; color: string; icon?: ReactNode }

/** Legend in text ink; the swatch carries identity (never coloured text). */
export function ChartLegend({ items }: { items: LegendItem[] }) {
  if (items.length < 2) return null
  return (
    <div className="flex flex-wrap items-center gap-x-4 gap-y-1 px-5 pb-1 text-xs text-ink-2">
      {items.map((i) => (
        <span key={i.label} className="inline-flex items-center gap-1.5">
          <span className="size-2.5 rounded-[3px]" style={{ background: i.color }} />
          {i.icon}
          {i.label}
        </span>
      ))}
    </div>
  )
}

/** HTML tooltip: label + one row per series, values in primary ink. */
interface TooltipEntry { dataKey?: unknown; name?: unknown; value?: unknown; color?: string }

export function ChartTooltip({
  active, payload, label, formatLabel, formatValue,
}: { active?: boolean; payload?: readonly TooltipEntry[]; label?: unknown; formatLabel?: (l: string) => string; formatValue?: (v: number) => string }) {
  if (!active || !payload?.length) return null
  return (
    <div className="min-w-40 rounded-xl border border-line bg-surface px-3 py-2 text-xs shadow-pop">
      <p className="mb-1.5 font-medium text-ink">{formatLabel ? formatLabel(String(label)) : String(label ?? '')}</p>
      <div className="space-y-1">
        {payload.map((p) => (
          <div key={String(p.dataKey)} className="flex items-center justify-between gap-4">
            <span className="inline-flex items-center gap-1.5 text-ink-2">
              <span className="size-2 rounded-[2px]" style={{ background: p.color }} />
              {String(p.name ?? '')}
            </span>
            <span className="tabular font-medium text-ink">{formatValue ? formatValue(Number(p.value)) : String(p.value ?? '')}</span>
          </div>
        ))}
      </div>
    </div>
  )
}

/** Card with a chart ⇄ table toggle (the table view is the accessible alternative for every chart). */
export function ChartCard({
  title, subtitle, legend, loading, empty, table, children, className, actions, height = 260,
}: {
  title: ReactNode; subtitle?: ReactNode; legend?: LegendItem[]; loading?: boolean; empty?: boolean
  table: { columns: string[]; rows: (string | number)[][] }; children: ReactNode; className?: string; actions?: ReactNode; height?: number
}) {
  const { t } = useTranslation()
  const [view, setView] = useState<'chart' | 'table'>('chart')
  return (
    <Card className={cn('flex flex-col', className)}>
      <CardHeader
        title={title}
        subtitle={subtitle}
        actions={
          <>
            {actions}
            <div className="inline-flex rounded-lg bg-surface-3 p-0.5">
              {(['chart', 'table'] as const).map((v) => (
                <button
                  key={v}
                  onClick={() => setView(v)}
                  aria-label={v === 'chart' ? t('common.chartView') : t('common.tableView')}
                  aria-pressed={view === v}
                  className={cn('rounded-md p-1.5 transition-colors', view === v ? 'bg-surface text-ink shadow-sm' : 'text-ink-3 hover:text-ink-2')}
                >
                  {v === 'chart' ? <BarChart3 className="size-3.5" /> : <Table2 className="size-3.5" />}
                </button>
              ))}
            </div>
          </>
        }
      />
      {loading ? (
        <div className="px-5 pb-5"><Skeleton style={{ height }} className="w-full" /></div>
      ) : empty ? (
        <EmptyState title={t('common.noResults')} className="py-10" />
      ) : view === 'chart' ? (
        <>
          {legend && <ChartLegend items={legend} />}
          <div className="px-2 pb-3" style={{ height }} dir="ltr">{children}</div>
        </>
      ) : (
        <div className="max-h-80 overflow-y-auto pb-2">
          <Table>
            <THead><tr>{table.columns.map((c) => <Th key={c}>{c}</Th>)}</tr></THead>
            <tbody>
              {table.rows.map((r, i) => (
                <Tr key={i}>{r.map((c, j) => <Td key={j} className={cn('tabular', j > 0 && 'text-end')}>{c}</Td>)}</Tr>
              ))}
            </tbody>
          </Table>
        </div>
      )}
    </Card>
  )
}

/** Discrete-stage funnel rendered as direct-labelled horizontal bars (no legend needed: rows are labelled). */
export function FunnelBars({ steps, formatValue }: { steps: { label: string; count: number; value: number }[]; formatValue: (v: number) => string }) {
  const max = Math.max(1, ...steps.map((s) => s.count))
  const first = steps[0]?.count || 1
  return (
    <div className="space-y-3 px-5 pb-5">
      {steps.map((s, i) => (
        <div key={s.label}>
          <div className="mb-1 flex items-baseline justify-between text-xs">
            <span className="font-medium text-ink">{s.label}</span>
            <span className="tabular text-ink-2">
              <span className="font-semibold text-ink">{s.count}</span>
              {i > 0 && <span className="ms-1.5 text-ink-3">({Math.round((100 * s.count) / first)}%)</span>}
              <span className="ms-2 text-ink-3">{formatValue(s.value)}</span>
            </span>
          </div>
          <div className="h-2.5 overflow-hidden rounded-full bg-surface-3">
            <div
              className="h-full rounded-full"
              style={{ width: `${(100 * s.count) / max}%`, background: `var(--series-1)`, opacity: 1 - i * 0.18 }}
            />
          </div>
        </div>
      ))}
    </div>
  )
}
