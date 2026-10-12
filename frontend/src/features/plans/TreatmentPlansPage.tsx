import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useQuery, keepPreviousData } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { ClipboardList, Download, Plus, Search, Sparkles } from 'lucide-react'
import { api } from '@/lib/api'
import { date, money } from '@/lib/format'
import { displayName, downloadCsv, Permissions } from '@/lib/utils'
import { useDentists } from '@/lib/queries'
import type { Paged, TreatmentPlanListItem, TreatmentPlanStatus } from '@/lib/types'
import { useAuth } from '@/auth/AuthProvider'
import { Badge, Card, EmptyState, ErrorState, Input, LoadingBlock, PageHeader, Pagination, ProgressBar, Select, Switch } from '@/components/ui/primitives'
import { Button } from '@/components/ui/Button'
import { Table, THead, Td, Th, Tr } from '@/components/ui/Table'
import { PlanStatusBadge } from '@/components/StatusBadge'
import { PlanFormDialog } from './PlanFormDialog'

const statuses: TreatmentPlanStatus[] = ['Draft', 'Presented', 'Accepted', 'InProgress', 'PartiallyCompleted', 'Completed', 'Rejected']

export default function TreatmentPlansPage() {
  const { t } = useTranslation()
  const { can } = useAuth()
  const navigate = useNavigate()
  const dentists = useDentists()
  const [search, setSearch] = useState('')
  const [debounced, setDebounced] = useState('')
  const [status, setStatus] = useState<TreatmentPlanStatus | ''>('')
  const [dentistId, setDentistId] = useState('')
  const [stalled, setStalled] = useState(false)
  const [page, setPage] = useState(1)
  const [creating, setCreating] = useState(false)

  useEffect(() => {
    const id = setTimeout(() => { setDebounced(search); setPage(1) }, 200)
    return () => clearTimeout(id)
  }, [search])

  const q = useQuery({
    queryKey: ['plans', debounced, status, dentistId, stalled, page],
    queryFn: () => api.get<Paged<TreatmentPlanListItem>>('/treatment-plans', { search: debounced, status, dentistId, stalled: stalled || undefined, page, pageSize: 25 }),
    placeholderData: keepPreviousData,
  })

  const exportCsv = () => downloadCsv('treatment-plans.csv',
    ['Plan', 'Patient', 'Title', 'Dentist', 'Status', 'Total', 'Paid', 'Remaining', 'Completion %', 'Created'],
    (q.data?.items ?? []).map((p) => [p.planNumber, p.patientName, p.title, p.dentistName, p.status, p.totalAmount, p.paidAmount, p.remainingAmount, p.completionPercent, p.createdAt.slice(0, 10)]))

  return (
    <div className="space-y-5">
      <PageHeader
        title={t('plans.title')}
        subtitle={q.data && t('common.showing', { count: q.data.totalCount })}
        actions={
          <>
            <Button variant="outline" icon={<Download className="size-4" />} onClick={exportCsv} disabled={!q.data?.items.length}>{t('common.export')}</Button>
            {can(Permissions.TreatmentManage) && <Button icon={<Plus className="size-4" />} onClick={() => setCreating(true)}>{t('plans.new')}</Button>}
          </>
        }
      />
      <Card>
        <div className="flex flex-wrap items-center gap-3 border-b border-line p-4">
          <div className="relative min-w-60 flex-1">
            <Search className="pointer-events-none absolute start-3 top-1/2 size-4 -translate-y-1/2 text-ink-3" />
            <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t('common.search')} className="ps-9" />
          </div>
          <Select value={status} onChange={(e) => { setStatus(e.target.value as TreatmentPlanStatus | ''); setPage(1) }} className="w-48">
            <option value="">{t('common.status')}: {t('common.all')}</option>
            {statuses.map((s) => <option key={s} value={s}>{t(`enum.TreatmentPlanStatus.${s}`)}</option>)}
          </Select>
          <Select value={dentistId} onChange={(e) => { setDentistId(e.target.value); setPage(1) }} className="w-48">
            <option value="">{t('appointments.allDentists')}</option>
            {dentists.data?.map((d) => <option key={d.id} value={d.id}>{d.fullName}</option>)}
          </Select>
          <Switch checked={stalled} onChange={(v) => { setStalled(v); setPage(1) }} label={<span className="inline-flex items-center gap-1 text-sm text-recovery-ink"><Sparkles className="size-3.5" />{t('plans.stalled')}</span>} />
        </div>
        {q.error ? <ErrorState error={q.error} onRetry={() => void q.refetch()} /> : q.isLoading ? <LoadingBlock rows={8} /> : !q.data?.items.length ? (
          <EmptyState icon={<ClipboardList className="size-6" />} title={t('plans.empty')} />
        ) : (
          <>
            <Table>
              <THead>
                <tr>
                  <Th>{t('plans.planTitle')}</Th><Th>{t('common.patient')}</Th><Th>{t('common.status')}</Th>
                  <Th className="text-end">{t('common.total')}</Th><Th className="text-end">{t('plans.remaining')}</Th>
                  <Th className="w-40">{t('plans.completion')}</Th><Th>{t('plans.nextAppointment')}</Th>
                </tr>
              </THead>
              <tbody>
                {q.data.items.map((p) => {
                  const stalledRow = !p.nextAppointmentAt && ['Presented', 'Accepted', 'InProgress', 'PartiallyCompleted'].includes(p.status)
                  return (
                    <Tr key={p.id} onClick={() => navigate(`/treatment-plans/${p.id}`)}>
                      <Td>
                        <p className="font-medium text-ink">{p.title}</p>
                        <p className="text-xs text-ink-3">{p.planNumber} · {p.dentistName} · {date(p.createdAt)}</p>
                      </Td>
                      <Td>{displayName(p.patientName, p.patientNameAr)}</Td>
                      <Td><PlanStatusBadge status={p.status} /></Td>
                      <Td className="tabular text-end">{money(p.totalAmount)}</Td>
                      <Td className="tabular text-end">{money(p.remainingAmount)}</Td>
                      <Td>
                        <div className="flex items-center gap-2">
                          <ProgressBar value={p.completionPercent} tone={p.status === 'Completed' ? 'good' : 'primary'} />
                          <span className="tabular w-9 text-end text-xs text-ink-3">{p.completionPercent}%</span>
                        </div>
                      </Td>
                      <Td>
                        {p.nextAppointmentAt ? date(p.nextAppointmentAt) : stalledRow
                          ? <Badge tone="recovery"><Sparkles className="size-3" />{t('plans.inactiveDays', { count: p.daysSinceActivity })}</Badge> : '—'}
                      </Td>
                    </Tr>
                  )
                })}
              </tbody>
            </Table>
            <Pagination page={page} totalPages={q.data.totalPages} onChange={setPage} />
          </>
        )}
      </Card>
      <PlanFormDialog open={creating} onClose={() => setCreating(false)} onSaved={(id) => navigate(`/treatment-plans/${id}`)} />
    </div>
  )
}
