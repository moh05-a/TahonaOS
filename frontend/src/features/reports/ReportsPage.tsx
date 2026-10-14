import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { ImpactCard } from './ImpactCard'
import { Download } from 'lucide-react'
import { api } from '@/lib/api'
import { isoDateInClinic, clinicLocalToUtc, money, number, percent } from '@/lib/format'
import { downloadCsv } from '@/lib/utils'
import type { Analytics, NamedValue } from '@/lib/types'
import { Card, CardHeader, ErrorState, PageHeader, Segmented, Skeleton } from '@/components/ui/primitives'
import { Button } from '@/components/ui/Button'
import { FunnelChart, RevenueChart } from '@/features/dashboard/DashboardCharts'

type Range = '3' | '6' | '12'

export default function ReportsPage() {
  const { t } = useTranslation()
  const [range, setRange] = useState<Range>('6')

  const { from, to } = useMemo(() => {
    const today = isoDateInClinic()
    const [y, m] = today.split('-').map(Number)
    const start = new Date(Date.UTC(y, m - Number(range), 1))
    const fromLocal = `${start.getUTCFullYear()}-${String(start.getUTCMonth() + 1).padStart(2, '0')}-01T00:00`
    return { from: clinicLocalToUtc(fromLocal), to: new Date().toISOString() }
  }, [range])

  const q = useQuery({ queryKey: ['analytics', from], queryFn: () => api.get<Analytics>('/reports/analytics', { from, to }) })
  const a = q.data

  const exportCsv = () => {
    if (!a) return
    const rows: (string | number)[][] = [
      ['Treatment plans', a.treatment.plansCreated], ['Accepted', a.treatment.plansAccepted], ['Started', a.treatment.plansStarted],
      ['Completed', a.treatment.plansCompleted], ['Abandoned', a.treatment.plansAbandoned], ['Acceptance rate %', a.treatment.acceptanceRate],
      ['Completion rate %', a.treatment.completionRate], ['Average treatment value', a.treatment.averageTreatmentValue],
      ['Patients seen', a.patients.patientsSeen], ['Return rate %', a.patients.returnRate], ['No-show rate %', a.patients.noShowRate],
      ['Revenue', a.financial.revenue], ['Expenses', a.financial.expenses], ['Net profit', a.financial.netProfit], ['Production', a.financial.production],
      ['Revenue recovered', a.recovery.revenueRecovered], ['Potential lost revenue', a.recovery.potentialLostRevenue], ['Recovery rate %', a.recovery.recoveryRate],
    ]
    downloadCsv(`tahona-report-${range}m.csv`, ['Metric', 'Value'], rows)
  }

  if (q.error) return <ErrorState error={q.error} onRetry={() => void q.refetch()} />

  return (
    <div className="space-y-6">
      <PageHeader
        title={t('reports.title')}
        subtitle={t('reports.subtitle')}
        actions={
          <>
            <Segmented<Range> value={range} onChange={setRange} items={[
              { value: '3', label: t('reports.last3') }, { value: '6', label: t('reports.last6') }, { value: '12', label: t('reports.last12') },
            ]} />
            <Button variant="outline" icon={<Download className="size-4" />} onClick={exportCsv} disabled={!a}>{t('common.export')}</Button>
          </>
        }
      />

      <ImpactCard />

      <Section title={t('reports.treatment')} loading={!a} items={a && [
        { label: t('reports.plansCreated'), value: number(a.treatment.plansCreated) },
        { label: t('reports.accepted'), value: number(a.treatment.plansAccepted) },
        { label: t('reports.started'), value: number(a.treatment.plansStarted) },
        { label: t('reports.completed'), value: number(a.treatment.plansCompleted) },
        { label: t('reports.abandoned'), value: number(a.treatment.plansAbandoned) },
        { label: t('reports.acceptanceRate'), value: percent(a.treatment.acceptanceRate), emphasis: true },
        { label: t('reports.completionRate'), value: percent(a.treatment.completionRate), emphasis: true },
        { label: t('reports.avgValue'), value: money(a.treatment.averageTreatmentValue) },
      ]} />

      <div className="grid gap-6 lg:grid-cols-2">
        <Section title={t('reports.recovery')} recovery loading={!a} items={a && [
          { label: t('reports.recovered'), value: money(a.recovery.revenueRecovered), emphasis: true },
          { label: t('reports.lost'), value: money(a.recovery.potentialLostRevenue) },
          { label: t('reports.recoveryRate'), value: percent(a.recovery.recoveryRate) },
          { label: t('recovery.toContact'), value: number(a.recovery.patientsContacted) },
        ]} />
        <Section title={t('reports.patients')} loading={!a} items={a && [
          { label: t('reports.patientsSeen'), value: number(a.patients.patientsSeen) },
          { label: t('reports.returnRate'), value: percent(a.patients.returnRate), emphasis: true },
          { label: t('reports.noShowRate'), value: percent(a.patients.noShowRate) },
          { label: t('dashboard.newPatients'), value: number(a.patients.newPatients) },
        ]} />
      </div>

      <Section title={t('reports.financial')} loading={!a} items={a && [
        { label: t('reports.revenue'), value: money(a.financial.revenue), emphasis: true },
        { label: t('reports.expenses'), value: money(a.financial.expenses) },
        { label: t('reports.net'), value: money(a.financial.netProfit), emphasis: true },
        { label: t('reports.production'), value: money(a.financial.production) },
        { label: t('reports.invoiced'), value: money(a.financial.invoiced) },
        { label: t('billing.outstanding'), value: money(a.financial.outstanding) },
      ]} />

      <div className="grid gap-6 lg:grid-cols-2">
        <RevenueChart data={a?.revenueByMonth} loading={!a} />
        <FunnelChart data={a?.funnel} loading={!a} title={t('reports.treatment')} subtitle={t('reports.plansCreated')} />
        <Ranked title={t('reports.byDentist')} data={a?.productionByDentist} />
        <Ranked title={t('reports.byCategory')} data={a?.productionByCategory} translate={(n) => t(`enum.ProcedureCategory.${n}`, { defaultValue: n })} />
        <Ranked title={t('reports.topProcedures')} data={a?.topProcedures} />
        <Ranked title={t('reports.paymentMethods')} data={a?.paymentMethods} translate={(n) => t(`enum.PaymentMethod.${n}`, { defaultValue: n })} />
        <Ranked title={t('reports.expensesByCategory')} data={a?.expensesByCategory} translate={(n) => t(`enum.ExpenseCategory.${n}`, { defaultValue: n })} color="var(--series-2)" />
        <Ranked title={t('reports.recoveryByIssue')} data={a?.recoveryByIssue} translate={(n) => t(`enum.RecoveryIssueType.${n}`, { defaultValue: n })} color="var(--recovery-from)" />
      </div>
    </div>
  )
}

function Section({ title, items, loading, recovery }: {
  title: string; items?: { label: string; value: string; emphasis?: boolean }[] | false; loading: boolean; recovery?: boolean
}) {
  return (
    <Card className={recovery ? 'border-recovery/30' : undefined}>
      <CardHeader title={<span className={recovery ? 'text-recovery-ink' : undefined}>{title}</span>} />
      <div className={`grid grid-cols-2 gap-px overflow-hidden rounded-b-2xl border-t border-line bg-line ${items && items.length % 4 !== 0 && items.length % 3 === 0 ? 'sm:grid-cols-3' : 'sm:grid-cols-4'}`}>
        {loading || !items ? Array.from({ length: 4 }).map((_, i) => <div key={i} className="bg-surface p-4"><Skeleton className="h-10" /></div>) : items.map((i) => (
          <div key={i.label} className="bg-surface p-4">
            <p className="text-xs text-ink-3">{i.label}</p>
            <p className={`tabular mt-1 text-lg font-semibold tracking-tight ${i.emphasis ? (recovery ? 'text-recovery-ink' : 'text-primary-ink') : 'text-ink'}`}>{i.value}</p>
          </div>
        ))}
      </div>
    </Card>
  )
}

/** Single-series ranked bars with direct labels (magnitude by length; no legend needed). */
function Ranked({ title, data, translate, color = 'var(--series-1)' }: { title: string; data?: NamedValue[]; translate?: (n: string) => string; color?: string }) {
  const { t } = useTranslation()
  const max = Math.max(1, ...(data ?? []).map((d) => d.value))
  return (
    <Card>
      <CardHeader title={title} />
      <div className="space-y-3 px-5 pb-5">
        {!data ? <Skeleton className="h-40" /> : data.length === 0 ? <p className="text-sm text-ink-3">{t('common.noResults')}</p> : data.slice(0, 8).map((d) => (
          <div key={d.name}>
            <div className="mb-1 flex justify-between gap-3 text-xs">
              <span className="truncate text-ink-2">{translate ? translate(d.name) : d.name} <span className="text-ink-3">({d.count})</span></span>
              <span className="tabular font-medium text-ink">{money(d.value)}</span>
            </div>
            <div className="h-2 overflow-hidden rounded-full bg-surface-3">
              <div className="h-full rounded-full" style={{ width: `${(100 * d.value) / max}%`, background: color }} />
            </div>
          </div>
        ))}
      </div>
    </Card>
  )
}
