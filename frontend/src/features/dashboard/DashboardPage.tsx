import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { ArrowRight, CalendarDays, CircleDollarSign, ClipboardList, Sparkles, Wallet } from 'lucide-react'
import { api } from '@/lib/api'
import { money, number, percent, time, weekday } from '@/lib/format'
import { cn, displayName, Permissions } from '@/lib/utils'
import type { Dashboard } from '@/lib/types'
import { useAuth } from '@/auth/AuthProvider'
import { Card, CardHeader, EmptyState, ErrorState, PageHeader, Skeleton, StatCard, Switch } from '@/components/ui/primitives'
import { Button } from '@/components/ui/Button'
import { AppointmentStatusBadge, IssueBadge, PriorityBadge } from '@/components/StatusBadge'
import { AppointmentsChart, FunnelChart, RecoveryTrendChart, RetentionChart, RevenueChart } from './DashboardCharts'
import { recoveryAction } from '@/features/recovery/recoveryText'

export default function DashboardPage() {
  const { t } = useTranslation()
  const { user, can } = useAuth()
  const navigate = useNavigate()
  const [mine, setMine] = useState(false)
  const { data, isLoading, error, refetch } = useQuery({
    queryKey: ['dashboard', mine],
    queryFn: () => api.get<Dashboard>('/dashboard', { mine }),
    refetchInterval: 120_000,
  })

  const hour = Number(new Intl.DateTimeFormat('en-GB', { hour: 'numeric', hourCycle: 'h23', timeZone: user?.clinic.timeZone }).format(new Date()))
  const part = hour < 12 ? t('dashboard.morning') : hour < 18 ? t('dashboard.afternoon') : t('dashboard.evening')
  const firstName = user ? displayName(user.fullName, user.fullNameAr).replace(/^(Dr\.?|د\.)\s*/, '').split(' ')[0] : ''

  if (error) return <ErrorState error={error} onRetry={() => void refetch()} />

  return (
    <div className="space-y-6">
      <PageHeader
        title={t('dashboard.greeting', { part, name: firstName })}
        subtitle={`${weekday(new Date())} · ${t('dashboard.subtitle')}`}
        actions={user?.dentistId && <Switch checked={mine} onChange={setMine} label={<span className="text-sm text-ink-2">{t('dashboard.mine')}</span>} />}
      />

      {/* KPI row */}
      <div className="grid grid-cols-2 gap-4 xl:grid-cols-4">
        {isLoading || !data ? (
          Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-28" />)
        ) : (
          <>
            <StatCard label={t('dashboard.todayAppointments')} value={number(data.todayAppointments)} icon={<CalendarDays className="size-4" />}
              hint={t('dashboard.completedRemaining', { completed: data.todayCompleted, remaining: data.todayRemaining })} onClick={() => navigate('/appointments')} />
            <StatCard label={t('dashboard.todayRevenue')} value={money(data.todayRevenue)} icon={<Wallet className="size-4" />} tone="good"
              hint={`${t('dashboard.newPatients')}: ${data.newPatientsThisMonth}`} onClick={can(Permissions.BillingView) ? () => navigate('/payments') : undefined} />
            <StatCard label={t('dashboard.outstanding')} value={money(data.outstandingPayments)} icon={<CircleDollarSign className="size-4" />} tone="warning"
              hint={t('dashboard.overdueInvoices', { count: data.overdueInvoices })} onClick={can(Permissions.BillingView) ? () => navigate('/invoices?overdue=1') : undefined} />
            <StatCard label={t('dashboard.potential')} value={money(data.treatmentRevenuePotential)} icon={<ClipboardList className="size-4" />} tone="info"
              hint={`${t('dashboard.openPlans', { count: data.openTreatmentPlans })} · ${t('dashboard.noShowRate')} ${percent(data.noShowRate30d)}`}
              onClick={can(Permissions.TreatmentView) ? () => navigate('/treatment-plans') : undefined} />
          </>
        )}
      </div>

      <div className="grid gap-6 xl:grid-cols-5">
        {/* Revenue Recovery hero */}
        {can(Permissions.RecoveryView) && (
          <Card className="overflow-hidden xl:col-span-3">
            <div className="bg-recovery-gradient p-5 text-white">
              <div className="flex items-center justify-between gap-3">
                <div className="flex items-center gap-2">
                  <span className="grid size-8 place-items-center rounded-lg bg-white/15"><Sparkles className="size-4" /></span>
                  <h2 className="font-semibold">{t('dashboard.recoveryCard')}</h2>
                </div>
                <Link to="/recovery" className="inline-flex items-center gap-1 text-xs font-medium text-white/90 hover:text-white">
                  {t('dashboard.openRecovery')} <ArrowRight className="size-3.5 rtl:rotate-180" />
                </Link>
              </div>
              <div className="mt-5 grid grid-cols-3 gap-4">
                {isLoading || !data ? (
                  Array.from({ length: 3 }).map((_, i) => <div key={i} className="h-14 animate-pulse rounded-lg bg-white/10" />)
                ) : (
                  <>
                    <HeroStat label={t('dashboard.potentialRevenue')} value={money(data.recovery.potentialRevenue)} />
                    <HeroStat label={t('dashboard.recoveredThisMonth')} value={money(data.recovery.recoveredThisMonth)} />
                    <HeroStat label={t('dashboard.requiringFollowUp')} value={number(data.recovery.patientsRequiringFollowUp)} />
                  </>
                )}
              </div>
            </div>
            <div className="p-2">
              <p className="px-3 pt-2 pb-1 text-xs font-semibold text-ink-2">{t('dashboard.contactToday')}</p>
              {isLoading || !data ? (
                <div className="space-y-2 p-3">{Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-11" />)}</div>
              ) : data.recovery.topPatients.length === 0 ? (
                <EmptyState icon={<Sparkles className="size-5" />} title={t('recovery.empty')} className="py-6" />
              ) : (
                <ul>
                  {data.recovery.topPatients.map((p) => (
                    <li key={p.patientId}>
                      <Link to={`/recovery?patient=${p.patientId}`} className="flex items-center gap-3 rounded-lg px-3 py-2.5 hover:bg-surface-2">
                        <span className="tabular grid size-9 shrink-0 place-items-center rounded-lg bg-recovery-soft text-xs font-bold text-recovery-ink">
                          {p.score.score}
                        </span>
                        <span className="min-w-0 flex-1">
                          <span className="block truncate text-sm font-medium text-ink">{displayName(p.fullName, p.fullNameAr)}</span>
                          <span className="block truncate text-xs text-ink-3">{recoveryAction(p, t)}</span>
                        </span>
                        <span className="hidden sm:block"><IssueBadge issue={p.primaryIssue} /></span>
                        <span className="tabular text-sm font-semibold text-ink">{money(p.totalValue)}</span>
                        <span className="hidden md:block"><PriorityBadge priority={p.score.priority} /></span>
                      </Link>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          </Card>
        )}

        {/* Today's schedule */}
        <Card className={cn('flex flex-col', can(Permissions.RecoveryView) ? 'xl:col-span-2' : 'xl:col-span-5')}>
          <CardHeader
            title={t('dashboard.schedule')}
            subtitle={data ? t('dashboard.completedRemaining', { completed: data.todayCompleted, remaining: data.todayRemaining }) : undefined}
            actions={<Button variant="outline" size="sm" onClick={() => navigate('/appointments')}>{t('common.viewAll')}</Button>}
          />
          <div className="max-h-[420px] flex-1 overflow-y-auto px-2 pb-2">
            {isLoading || !data ? (
              <div className="space-y-2 p-3">{Array.from({ length: 5 }).map((_, i) => <Skeleton key={i} className="h-12" />)}</div>
            ) : data.todaySchedule.length === 0 ? (
              <EmptyState icon={<CalendarDays className="size-5" />} title={t('dashboard.noAppointmentsToday')} className="py-10" />
            ) : (
              <ul>
                {data.todaySchedule.map((a) => (
                  <li key={a.id}>
                    <Link to={`/patients/${a.patientId}`} className="flex items-center gap-3 rounded-lg px-3 py-2.5 hover:bg-surface-2">
                      <span className="tabular w-14 shrink-0 text-xs font-medium text-ink-2">{time(a.startTime)}</span>
                      <span className="h-8 w-1 shrink-0 rounded-full" style={{ background: a.dentistColor }} />
                      <span className="min-w-0 flex-1">
                        <span className="block truncate text-sm font-medium text-ink">{displayName(a.patientName, a.patientNameAr)}</span>
                        <span className="block truncate text-xs text-ink-3">{a.treatment} · {a.dentistName}</span>
                      </span>
                      <AppointmentStatusBadge status={a.status} />
                    </Link>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </Card>
      </div>

      <div className="grid gap-6 lg:grid-cols-2">
        <RevenueChart data={data?.revenueChart} loading={isLoading} />
        <AppointmentsChart data={data?.appointmentsChart} loading={isLoading} />
        <FunnelChart data={data?.treatmentFunnel} loading={isLoading} />
        <RetentionChart data={data?.retentionChart} loading={isLoading} />
        {can(Permissions.RecoveryView) && <RecoveryTrendChart data={data?.recoveryChart} loading={isLoading} />}
      </div>
    </div>
  )
}

function HeroStat({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <p className="text-[11px] font-medium text-white/75">{label}</p>
      <p className="tabular mt-1 text-xl font-semibold tracking-tight sm:text-2xl">{value}</p>
    </div>
  )
}
