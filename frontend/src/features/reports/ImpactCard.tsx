import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { ArrowDownRight, ArrowUpRight, Info, Minus } from 'lucide-react'
import { api } from '@/lib/api'
import { date, money, number } from '@/lib/format'
import type { ImpactPeriod, ImpactReport } from '@/lib/types'
import { Card, CardHeader, ErrorState, LoadingBlock } from '@/components/ui/primitives'
import { Table, Td, Th, THead, Tr } from '@/components/ui/Table'

type Format = 'count' | 'money' | 'percent'
/** Whether a higher value is the better direction for a row; undefined when neither is "better". */
const rows: { key: keyof ImpactPeriod; format: Format; higherIsBetter?: boolean }[] = [
  { key: 'followUpsContacted', format: 'count', higherIsBetter: true },
  { key: 'bookedAfterContact', format: 'count', higherIsBetter: true },
  { key: 'paidAfterContact', format: 'count', higherIsBetter: true },
  { key: 'collectedAfterFollowUp', format: 'money', higherIsBetter: true },
  { key: 'remindersSent', format: 'count' },
  { key: 'noShowRate', format: 'percent', higherIsBetter: false },
  { key: 'appointmentsCompleted', format: 'count', higherIsBetter: true },
  { key: 'returningPatients', format: 'count', higherIsBetter: true },
  { key: 'plansPresented', format: 'count' },
  { key: 'plansAccepted', format: 'count', higherIsBetter: true },
  { key: 'collected', format: 'money', higherIsBetter: true },
]

/**
 * Follow-up activity and what followed it, for the last 30 days beside the 30 days before.
 * Observed counts only: it states what was recorded, not what the follow-ups caused.
 */
export function ImpactCard() {
  const { t } = useTranslation()
  const q = useQuery({ queryKey: ['reports', 'impact'], queryFn: () => api.get<ImpactReport>('/reports/impact') })
  const show = (v: number, f: Format) => (f === 'money' ? money(v) : f === 'percent' ? `${number(v, 1)}%` : number(v))

  return (
    <Card>
      <CardHeader title={t('reports.impact.title')} subtitle={t('reports.impact.subtitle')} />
      {q.error ? <ErrorState error={q.error} onRetry={() => void q.refetch()} /> : !q.data ? <LoadingBlock rows={5} /> : (
        <>
          <Table>
            <THead>
              <tr>
                <Th>{t('reports.impact.measure')}</Th>
                <Th className="text-end">{t('reports.impact.current')}<span className="block text-[11px] font-normal text-ink-3">{date(q.data.current.from)} – {date(q.data.current.to)}</span></Th>
                <Th className="text-end">{t('reports.impact.previous')}<span className="block text-[11px] font-normal text-ink-3">{date(q.data.previous.from)} – {date(q.data.previous.to)}</span></Th>
                <Th className="text-end">{t('reports.impact.change')}</Th>
              </tr>
            </THead>
            <tbody>
              {rows.map((r) => {
                const now = q.data.current[r.key] as number
                const before = q.data.previous[r.key] as number
                const diff = now - before
                const better = r.higherIsBetter === undefined || diff === 0 ? null : (diff > 0) === r.higherIsBetter
                const Icon = diff === 0 ? Minus : diff > 0 ? ArrowUpRight : ArrowDownRight
                return (
                  <Tr key={r.key}>
                    <Td className="text-sm text-ink">{t(`reports.impact.rows.${r.key}`)}</Td>
                    <Td className="tabular text-end text-sm font-medium text-ink">{show(now, r.format)}</Td>
                    <Td className="tabular text-end text-sm text-ink-2">{show(before, r.format)}</Td>
                    <Td className="text-end">
                      <span className={`tabular inline-flex items-center gap-1 text-sm ${better === null ? 'text-ink-3' : better ? 'text-good-ink' : 'text-critical-ink'}`}>
                        <Icon className="size-3.5" aria-hidden />{diff > 0 ? '+' : ''}{show(diff, r.format)}
                      </span>
                    </Td>
                  </Tr>
                )
              })}
            </tbody>
          </Table>
          <div className="space-y-1 border-t border-line px-5 py-3 text-xs text-ink-3">
            <p className="flex gap-1.5"><Info className="mt-0.5 size-3.5 shrink-0" />{t('reports.impact.caveat1')}</p>
            <p className="ps-5">{t('reports.impact.caveat2')}</p>
          </div>
        </>
      )}
    </Card>
  )
}
