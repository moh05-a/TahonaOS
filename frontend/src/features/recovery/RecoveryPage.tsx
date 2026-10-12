import { useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import {
  CalendarPlus, CalendarX, CircleDollarSign, Clock, Download, Gem, MessageCircle, RefreshCw, RotateCcw, Search, Sparkles, Stethoscope, Trophy, UserRound, Users,
} from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { money, number, percent, relative } from '@/lib/format'
import { cn, displayName, downloadCsv, Permissions } from '@/lib/utils'
import type { PriorityLevel, RecoveryDashboard, RecoveryIssueType, RecoveryPatient } from '@/lib/types'
import { useAuth } from '@/auth/AuthProvider'
import { Badge, Card, CardHeader, EmptyState, ErrorState, Input, LoadingBlock, PageHeader, Segmented, Select, Skeleton } from '@/components/ui/primitives'
import { Button } from '@/components/ui/Button'
import { Table, THead, Td, Th, Tr } from '@/components/ui/Table'
import { IssueBadge, PriorityBadge } from '@/components/StatusBadge'
import { AppointmentDialog, type AppointmentDraft } from '@/features/appointments/AppointmentDialog'
import { RecoveryTrendChart } from '@/features/dashboard/DashboardCharts'
import { ContactDialog } from './ContactDialog'
import { RecoveryPatientPanel } from './RecoveryPatientPanel'
import { recoveryAction } from '@/features/recovery/recoveryText'

type View = 'due' | 'waiting' | 'all'

const issueIcons: Record<RecoveryIssueType, React.ElementType> = {
  TreatmentNotStarted: Sparkles, TreatmentUnfinished: Stethoscope, MissedAppointment: CalendarX, OutstandingBalance: CircleDollarSign,
  OverdueFollowUp: Clock, RecallDue: RotateCcw,
}

export default function RecoveryPage() {
  const { t } = useTranslation()
  const { can } = useAuth()
  const qc = useQueryClient()
  const canManage = can(Permissions.RecoveryManage)
  const [params, setParams] = useSearchParams()
  const [view, setView] = useState<View>('due')
  const [issue, setIssue] = useState<RecoveryIssueType | ''>('')
  const [priority, setPriority] = useState<PriorityLevel | ''>('')
  const [search, setSearch] = useState('')
  const [contactFor, setContactFor] = useState<RecoveryPatient | null>(null)
  const [booking, setBooking] = useState<AppointmentDraft | null>(null)

  const { data, isLoading, error, refetch } = useQuery({
    queryKey: ['recovery', 'dashboard'],
    queryFn: () => api.get<RecoveryDashboard>('/recovery/dashboard'),
    refetchInterval: 5 * 60_000,
  })

  const sync = useMutation({
    mutationFn: () => api.post<{ created: number }>('/recovery/sync'),
    onSuccess: (r) => {
      toast.success(t('recovery.synced', { created: r.created }))
      void qc.invalidateQueries({ queryKey: ['recovery'] })
      void qc.invalidateQueries({ queryKey: ['notifications'] })
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  const patients = useMemo(() => {
    let list = data?.patients ?? []
    if (view === 'due') list = list.filter((p) => p.isDue)
    if (view === 'waiting') list = list.filter((p) => !p.isDue)
    if (issue) list = list.filter((p) => p.issues.some((i) => i.issueType === issue))
    if (priority) list = list.filter((p) => p.score.priority === priority)
    const q = search.trim().toLowerCase()
    if (q) list = list.filter((p) => p.fullName.toLowerCase().includes(q) || p.fullNameAr?.includes(q) || p.phone.includes(q))
    return list
  }, [data, view, issue, priority, search])

  const selected = useMemo(() => data?.patients.find((p) => p.patientId === params.get('patient')) ?? null, [data, params])
  const openPanel = (p: RecoveryPatient | null) => {
    const next = new URLSearchParams(params)
    if (p) next.set('patient', p.patientId)
    else next.delete('patient')
    setParams(next, { replace: true })
  }

  const book = (p: RecoveryPatient) => {
    openPanel(null)
    setBooking({
      patient: { id: p.patientId, fullName: p.fullName, fullNameAr: p.fullNameAr, phone: p.phone },
      dentistId: p.suggestedDentistId, treatment: p.suggestedTreatment ?? '', treatmentPlanId: p.suggestedPlanId,
      followUpId: p.issues.find((i) => i.followUpId)?.followUpId, fromRecovery: true,
    })
  }

  if (error) return <ErrorState error={error} onRetry={() => void refetch()} />
  const s = data?.summary

  const exportCsv = () =>
    downloadCsv('revenue-recovery.csv',
      ['Patient', 'Phone', 'Primary issue', 'Value', 'Days since visit', 'Score', 'Priority', 'Stage', 'Recommended action'],
      patients.map((p) => [p.fullName, p.phone, p.primaryIssue, p.totalValue, p.daysSinceVisit ?? '', p.score.score, p.score.priority, p.stage, p.recommendedAction]))

  const chips: { key: RecoveryIssueType; label: string; count?: number; value?: number }[] = [
    { key: 'TreatmentNotStarted', label: t('recovery.notStarted'), count: s?.notStartedTreatments },
    { key: 'TreatmentUnfinished', label: t('recovery.unfinished'), count: s?.unfinishedTreatments },
    { key: 'MissedAppointment', label: t('recovery.missed'), count: s?.missedAppointments },
    { key: 'OutstandingBalance', label: t('recovery.balances'), count: s?.outstandingBalanceCount },
    { key: 'OverdueFollowUp', label: t('recovery.overdueFollowUps'), count: s?.overdueFollowUps },
    { key: 'RecallDue', label: t('recovery.recalls'), count: s?.recallsDue },
  ]

  return (
    <div className="space-y-6">
      <PageHeader
        icon={<span className="grid size-11 place-items-center rounded-2xl bg-recovery-gradient text-white shadow-sm"><Sparkles className="size-5" /></span>}
        title={t('recovery.title')}
        subtitle={t('recovery.subtitle')}
        actions={
          <>
            <Button variant="outline" icon={<Download className="size-4" />} onClick={exportCsv} disabled={!patients.length}>{t('common.export')}</Button>
            {canManage && (
              <Button variant="outline" icon={<RefreshCw className={cn('size-4', sync.isPending && 'animate-spin')} />} onClick={() => sync.mutate()} disabled={sync.isPending}>
                {t('recovery.runSync')}
              </Button>
            )}
          </>
        }
      />

      {/* Headline numbers */}
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        {!s ? (
          Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-32" />)
        ) : (
          <>
            <div className="relative overflow-hidden rounded-2xl bg-recovery-gradient p-5 text-white shadow-card">
              <p className="text-xs font-medium text-white/80">{t('recovery.potential')}</p>
              <p className="tabular mt-2 text-3xl font-semibold tracking-tight">{money(s.potentialRevenue)}</p>
              <p className="mt-1 text-xs text-white/80">{t('recovery.dueValue', { value: money(s.dueTodayValue) })}</p>
              <Sparkles className="absolute -end-3 -bottom-3 size-20 text-white/10" />
            </div>
            <HeadlineCard icon={<Trophy className="size-4" />} tone="good" label={t('recovery.recovered')} value={money(s.recoveredThisMonth)}
              hint={t('recovery.vsLastMonth', { value: money(s.recoveredLastMonth) })} />
            <HeadlineCard icon={<Users className="size-4" />} tone="recovery" label={t('recovery.toContact')} value={number(s.patientsToContact)}
              hint={`${number(s.patientsInProgress)} ${t('recovery.inProgress').toLowerCase()} · ${number(s.highValuePatients)} ${t('recovery.highValue').toLowerCase()}`} />
            <HeadlineCard icon={<Gem className="size-4" />} tone="primary" label={t('recovery.recoveryRate')} value={percent(s.recoveryRate)}
              hint={`${t('recovery.balances')}: ${money(s.outstandingBalanceTotal)}`} />
          </>
        )}
      </div>

      {/* Issue chips = filters */}
      <div className="flex flex-wrap gap-2">
        {chips.map((c) => {
          const Icon = issueIcons[c.key]
          const active = issue === c.key
          return (
            <button
              key={c.key}
              onClick={() => setIssue(active ? '' : c.key)}
              className={cn(
                'inline-flex items-center gap-2 rounded-xl border px-3 py-2 text-sm transition-colors',
                active ? 'border-recovery bg-recovery-soft text-recovery-ink' : 'border-line bg-surface text-ink-2 hover:border-line-strong',
              )}
              aria-pressed={active}
            >
              <Icon className="size-4" />
              {c.label}
              <span className="tabular rounded-md bg-surface-3 px-1.5 text-xs font-semibold text-ink">{c.count ?? '–'}</span>
            </button>
          )
        })}
      </div>

      <div className="grid gap-6 min-[1680px]:grid-cols-[1fr_340px]">
        {/* Action list */}
        <Card className="min-w-0">
          <div className="flex flex-wrap items-center gap-3 border-b border-line px-5 py-3">
            <Segmented<View>
              value={view}
              onChange={setView}
              items={[
                { value: 'due', label: `${t('recovery.filters.due')} (${s?.patientsToContact ?? 0})` },
                { value: 'waiting', label: `${t('recovery.filters.waiting')} (${s?.patientsInProgress ?? 0})` },
                { value: 'all', label: t('common.all') },
              ]}
            />
            <Select value={priority} onChange={(e) => setPriority(e.target.value as PriorityLevel | '')} className="h-8 w-auto text-xs">
              <option value="">{t('common.priority')}: {t('common.all')}</option>
              {(['High', 'Medium', 'Low'] as PriorityLevel[]).map((p) => <option key={p} value={p}>{t(`enum.PriorityLevel.${p}`)}</option>)}
            </Select>
            <div className="relative ms-auto w-full sm:w-56">
              <Search className="pointer-events-none absolute start-2.5 top-1/2 size-3.5 -translate-y-1/2 text-ink-3" />
              <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t('common.search')} className="h-8 ps-8 text-xs" />
            </div>
          </div>

          {isLoading ? (
            <LoadingBlock rows={8} />
          ) : patients.length === 0 ? (
            <EmptyState icon={<Trophy className="size-6" />} title={t('recovery.empty')} body={t('recovery.emptyBody')} />
          ) : (
            <Table>
              <THead>
                <tr>
                  <Th>{t('recovery.table.patient')}</Th>
                  <Th>{t('recovery.table.issue')}</Th>
                  <Th className="text-end">{t('recovery.table.value')}</Th>
                  <Th className="text-end">{t('recovery.table.daysSince')}</Th>
                  <Th>{t('recovery.table.priority')}</Th>
                  <Th className="text-end">{t('recovery.table.action')}</Th>
                </tr>
              </THead>
              <tbody>
                {patients.map((p) => (
                  <Tr key={p.patientId} onClick={() => openPanel(p)}>
                    <Td>
                      <div className="flex items-center gap-3">
                        <span className="tabular grid size-9 shrink-0 place-items-center rounded-lg bg-recovery-soft text-xs font-bold text-recovery-ink" title={t('common.score')}>
                          {p.score.score}
                        </span>
                        <div className="min-w-0">
                          <p className="truncate font-medium text-ink">{displayName(p.fullName, p.fullNameAr)}</p>
                          <p className="truncate text-xs text-ink-3">
                            {p.stage === 'New' ? <span className="ltr">{p.phone}</span> : (
                              <>{t(`recovery.stage.${p.stage}`)}{p.lastContactedAt && ` · ${relative(p.lastContactedAt)}`}</>
                            )}
                          </p>
                        </div>
                      </div>
                    </Td>
                    <Td>
                      <div className="flex flex-wrap items-center gap-1.5">
                        <IssueBadge issue={p.primaryIssue} />
                        {p.issues.length > 1 && <Badge>{t('common.more', { count: p.issues.length - 1 })}</Badge>}
                      </div>
                      <p className="mt-1 max-w-[260px] truncate text-xs text-ink-3" title={recoveryAction(p, t)}>{recoveryAction(p, t)}</p>
                    </Td>
                    <Td className="tabular text-end font-semibold text-ink">{money(p.totalValue)}</Td>
                    <Td className="tabular text-end text-ink-2">{p.daysSinceVisit != null ? `${p.daysSinceVisit} ${t('common.days')}` : '—'}</Td>
                    <Td><PriorityBadge priority={p.score.priority} /></Td>
                    <Td className="text-end" onClick={(e) => e.stopPropagation()}>
                      <div className="flex justify-end gap-1">
                        {canManage && (
                          <>
                            <Button size="sm" variant="recovery" icon={<MessageCircle className="size-3.5" />} onClick={() => setContactFor(p)}>{t('recovery.contact')}</Button>
                            <Button size="icon" variant="outline" className="size-8" title={t('recovery.book')} aria-label={t('recovery.book')} onClick={() => book(p)}>
                              <CalendarPlus className="size-3.5" />
                            </Button>
                          </>
                        )}
                        <Link to={`/patients/${p.patientId}`} title={t('recovery.viewPatient')} aria-label={t('recovery.viewPatient')}>
                          <Button size="icon" variant="ghost" className="size-8" tabIndex={-1}><UserRound className="size-3.5" /></Button>
                        </Link>
                      </div>
                    </Td>
                  </Tr>
                ))}
              </tbody>
            </Table>
          )}
        </Card>

        {/* Side insights */}
        <div className="space-y-6">
          <Card>
            <CardHeader title={t('recovery.byIssue')} />
            <div className="space-y-3 px-5 pb-5">
              {!s ? <Skeleton className="h-40" /> : s.byIssue.map((b) => {
                const max = Math.max(...s.byIssue.map((x) => x.value), 1)
                return (
                  <button key={b.issueType} className="block w-full text-start" onClick={() => setIssue(b.issueType)}>
                    <div className="mb-1 flex justify-between text-xs">
                      <span className="text-ink-2">{t(`enum.RecoveryIssueType.${b.issueType}`)} <span className="text-ink-3">({b.count})</span></span>
                      <span className="tabular font-medium text-ink">{money(b.value)}</span>
                    </div>
                    <div className="h-2 overflow-hidden rounded-full bg-surface-3">
                      <div className="h-full rounded-full bg-recovery-gradient" style={{ width: `${(100 * b.value) / max}%` }} />
                    </div>
                  </button>
                )
              })}
            </div>
          </Card>

          <Card>
            <CardHeader title={t('recovery.wins')} />
            <div className="px-2 pb-3">
              {!data ? <Skeleton className="mx-3 h-32" /> : data.recentWins.length === 0 ? (
                <p className="px-3 pb-3 text-sm text-ink-3">{t('recovery.noWins')}</p>
              ) : data.recentWins.map((w, i) => (
                <Link key={i} to={`/patients/${w.patientId}`} className="flex items-center gap-3 rounded-lg px-3 py-2 hover:bg-surface-2">
                  <span className="grid size-8 place-items-center rounded-lg bg-good-soft text-good-ink"><Trophy className="size-4" /></span>
                  <span className="min-w-0 flex-1">
                    <span className="block truncate text-sm font-medium text-ink">{w.patientName}</span>
                    <span className="block truncate text-xs text-ink-3">{t(`enum.RecoveryIssueType.${w.issueType}`)} · {relative(w.resolvedAt)}{w.contactedBy && ` · ${w.contactedBy}`}</span>
                  </span>
                  <span className="tabular text-sm font-semibold text-good-ink">+{money(w.amount)}</span>
                </Link>
              ))}
            </div>
          </Card>

          <RecoveryTrendChart data={data?.trend} loading={isLoading} title={t('recovery.trend')} />
        </div>
      </div>

      <RecoveryPatientPanel
        patient={selected}
        onClose={() => openPanel(null)}
        onContact={(p) => { openPanel(null); setContactFor(p) }}
        onBook={book}
        canManage={canManage}
      />
      <ContactDialog patient={contactFor} onClose={() => setContactFor(null)} />
      <AppointmentDialog open={!!booking} draft={booking ?? {}} onClose={() => setBooking(null)} />
    </div>
  )
}

function HeadlineCard({ icon, label, value, hint, tone }: { icon: React.ReactNode; label: string; value: string; hint: string; tone: 'good' | 'recovery' | 'primary' }) {
  const toneClass = tone === 'good' ? 'bg-good-soft text-good-ink' : tone === 'recovery' ? 'bg-recovery-soft text-recovery-ink' : 'bg-primary-soft text-primary-ink'
  return (
    <div className="rounded-2xl border border-line bg-surface p-5 shadow-card">
      <div className="flex items-center justify-between">
        <p className="text-xs font-medium text-ink-3">{label}</p>
        <span className={cn('grid size-8 place-items-center rounded-lg', toneClass)}>{icon}</span>
      </div>
      <p className="tabular mt-2 text-3xl font-semibold tracking-tight text-ink">{value}</p>
      <p className="mt-1 truncate text-xs text-ink-3">{hint}</p>
    </div>
  )
}
