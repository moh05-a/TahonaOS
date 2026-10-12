import { useTranslation } from 'react-i18next'
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { CheckCircle2, CircleSlash, Clock, XCircle } from 'lucide-react'
import { axisProps, ChartCard, ChartTooltip, FunnelBars, useChartColors } from '@/components/charts/ChartKit'
import { money, monthLabel, number, shortDate } from '@/lib/format'
import type { Dashboard } from '@/lib/types'

const compactMoney = (v: number) => money(v, { compact: true })

export function RevenueChart({ data, loading }: { data?: Dashboard['revenueChart']; loading?: boolean }) {
  const { t } = useTranslation()
  const c = useChartColors()
  const rows = data ?? []
  return (
    <ChartCard
      title={t('dashboard.revenueChart')}
      subtitle={t('dashboard.revenueSub')}
      loading={loading}
      empty={!loading && rows.every((r) => r.revenue === 0 && r.expenses === 0)}
      legend={[{ label: t('reports.revenue'), color: c.series[0] }, { label: t('reports.expenses'), color: c.series[1] }]}
      table={{
        columns: [t('common.date'), t('reports.revenue'), t('reports.expenses'), t('reports.recovered')],
        rows: rows.map((r) => [monthLabel(r.month), money(r.revenue), money(r.expenses), money(r.recovered)]),
      }}
    >
      <ResponsiveContainer>
        <BarChart data={rows} barGap={2} barCategoryGap="22%" margin={{ top: 8, right: 8, left: 4, bottom: 0 }}>
          <CartesianGrid vertical={false} stroke={c.grid} />
          <XAxis dataKey="month" tickFormatter={monthLabel} {...axisProps(c.axis)} />
          <YAxis tickFormatter={(v) => number(v / 1000, 1) + 'k'} width={40} {...axisProps(c.axis)} />
          <Tooltip cursor={{ fill: c.grid, opacity: 0.5 }} content={(p) => <ChartTooltip {...p} formatLabel={monthLabel} formatValue={money} />} />
          <Bar dataKey="revenue" name={t('reports.revenue')} fill={c.series[0]} radius={[4, 4, 0, 0]} maxBarSize={22} />
          <Bar dataKey="expenses" name={t('reports.expenses')} fill={c.series[1]} radius={[4, 4, 0, 0]} maxBarSize={22} />
        </BarChart>
      </ResponsiveContainer>
    </ChartCard>
  )
}

export function AppointmentsChart({ data, loading }: { data?: Dashboard['appointmentsChart']; loading?: boolean }) {
  const { t } = useTranslation()
  const c = useChartColors()
  const rows = data ?? []
  // Appointment outcomes are states, so they use the status palette with icons in the legend.
  const series = [
    { key: 'completed', label: t('enum.AppointmentStatus.Completed'), color: c.good, icon: <CheckCircle2 className="size-3 text-ink-3" /> },
    { key: 'upcoming', label: t('appointments.upcoming'), color: c.series[0], icon: <Clock className="size-3 text-ink-3" /> },
    { key: 'noShow', label: t('enum.AppointmentStatus.NoShow'), color: c.critical, icon: <XCircle className="size-3 text-ink-3" /> },
    { key: 'cancelled', label: t('enum.AppointmentStatus.Cancelled'), color: c.neutral, icon: <CircleSlash className="size-3 text-ink-3" /> },
  ] as const
  return (
    <ChartCard
      title={t('dashboard.appointmentsChart')}
      subtitle={t('dashboard.appointmentsSub')}
      loading={loading}
      legend={series.map((s) => ({ label: s.label, color: s.color, icon: s.icon }))}
      table={{
        columns: [t('common.date'), ...series.map((s) => s.label)],
        rows: rows.map((r) => [shortDate(r.weekStart), r.completed, r.upcoming, r.noShow, r.cancelled]),
      }}
    >
      <ResponsiveContainer>
        <BarChart data={rows} barCategoryGap="28%" margin={{ top: 8, right: 8, left: -12, bottom: 0 }}>
          <CartesianGrid vertical={false} stroke={c.grid} />
          <XAxis dataKey="weekStart" tickFormatter={(v) => shortDate(v)} {...axisProps(c.axis)} />
          <YAxis allowDecimals={false} width={36} {...axisProps(c.axis)} />
          <Tooltip cursor={{ fill: c.grid, opacity: 0.5 }} content={(p) => <ChartTooltip {...p} formatLabel={(l) => shortDate(l)} />} />
          {series.map((s, i) => (
            <Bar key={s.key} dataKey={s.key} name={s.label} stackId="a" fill={s.color} stroke={c.surface} strokeWidth={2}
              radius={i === series.length - 1 ? [4, 4, 0, 0] : 0} maxBarSize={28} />
          ))}
        </BarChart>
      </ResponsiveContainer>
    </ChartCard>
  )
}

export function FunnelChart({ data, loading, title, subtitle }: { data?: Dashboard['treatmentFunnel']; loading?: boolean; title?: string; subtitle?: string }) {
  const { t } = useTranslation()
  const rows = data ?? []
  const labels: Record<string, string> = {
    Presented: t('enum.TreatmentPlanStatus.Presented'), Accepted: t('enum.TreatmentPlanStatus.Accepted'),
    Started: t('reports.started'), Completed: t('enum.TreatmentPlanStatus.Completed'),
  }
  return (
    <ChartCard
      title={title ?? t('dashboard.funnelChart')}
      subtitle={subtitle ?? t('dashboard.funnelSub')}
      loading={loading}
      empty={!loading && rows.every((r) => r.count === 0)}
      table={{ columns: [t('common.status'), t('reports.plansCreated'), t('common.value')], rows: rows.map((r) => [labels[r.step] ?? r.step, r.count, money(r.value)]) }}
      height={210}
    >
      <FunnelBars steps={rows.map((r) => ({ label: labels[r.step] ?? r.step, count: r.count, value: r.value }))} formatValue={compactMoney} />
    </ChartCard>
  )
}

export function RetentionChart({ data, loading }: { data?: Dashboard['retentionChart']; loading?: boolean }) {
  const { t } = useTranslation()
  const c = useChartColors()
  const rows = data ?? []
  return (
    <ChartCard
      title={t('dashboard.retentionChart')}
      subtitle={t('dashboard.retentionSub')}
      loading={loading}
      legend={[{ label: t('reports.returning'), color: c.series[0] }, { label: t('reports.new'), color: c.series[2] }]}
      table={{ columns: [t('common.date'), t('reports.returning'), t('reports.new')], rows: rows.map((r) => [monthLabel(r.month), r.returningPatients, r.newPatients]) }}
    >
      <ResponsiveContainer>
        <BarChart data={rows} barCategoryGap="30%" margin={{ top: 8, right: 8, left: -12, bottom: 0 }}>
          <CartesianGrid vertical={false} stroke={c.grid} />
          <XAxis dataKey="month" tickFormatter={monthLabel} {...axisProps(c.axis)} />
          <YAxis allowDecimals={false} width={36} {...axisProps(c.axis)} />
          <Tooltip cursor={{ fill: c.grid, opacity: 0.5 }} content={(p) => <ChartTooltip {...p} formatLabel={monthLabel} />} />
          <Bar dataKey="returningPatients" name={t('reports.returning')} stackId="r" fill={c.series[0]} stroke={c.surface} strokeWidth={2} maxBarSize={30} />
          <Bar dataKey="newPatients" name={t('reports.new')} stackId="r" fill={c.series[2]} stroke={c.surface} strokeWidth={2} radius={[4, 4, 0, 0]} maxBarSize={30} />
        </BarChart>
      </ResponsiveContainer>
    </ChartCard>
  )
}

export function RecoveryTrendChart({ data, loading, title, subtitle }: { data?: Dashboard['recoveryChart']; loading?: boolean; title?: string; subtitle?: string }) {
  const { t } = useTranslation()
  const c = useChartColors()
  const rows = data ?? []
  return (
    <ChartCard
      title={title ?? t('dashboard.recoveryChart')}
      subtitle={subtitle ?? t('dashboard.recoverySub')}
      loading={loading}
      table={{
        columns: [t('common.date'), t('reports.recovered'), t('recovery.toContact'), t('reports.patients')],
        rows: rows.map((r) => [monthLabel(r.period), money(r.recovered), r.patientsContacted, r.patientsRecovered]),
      }}
    >
      <ResponsiveContainer>
        <BarChart data={rows} barCategoryGap="30%" margin={{ top: 8, right: 8, left: 4, bottom: 0 }}>
          <CartesianGrid vertical={false} stroke={c.grid} />
          <XAxis dataKey="period" tickFormatter={monthLabel} {...axisProps(c.axis)} />
          <YAxis tickFormatter={(v) => number(v)} width={40} {...axisProps(c.axis)} />
          <Tooltip cursor={{ fill: c.grid, opacity: 0.5 }} content={(p) => <ChartTooltip {...p} formatLabel={monthLabel} formatValue={money} />} />
          <Bar dataKey="recovered" name={t('reports.recovered')} fill={c.recovery} radius={[4, 4, 0, 0]} maxBarSize={30} />
        </BarChart>
      </ResponsiveContainer>
    </ChartCard>
  )
}
