import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ArrowLeft, CalendarPlus, Check, FileText, Pencil, X } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { date, dateTime, money } from '@/lib/format'
import { displayName, Permissions } from '@/lib/utils'
import type { TreatmentPlanDetail, TreatmentPlanStatus } from '@/lib/types'
import { useAuth } from '@/auth/AuthProvider'
import { Card, CardHeader, ErrorState, KeyValue, ProgressBar, Skeleton, StatCard, Textarea } from '@/components/ui/primitives'
import { Button } from '@/components/ui/Button'
import { ConfirmDialog, Dialog } from '@/components/ui/Dialog'
import { Table, THead, Td, Th, Tr } from '@/components/ui/Table'
import { AppointmentStatusBadge, InvoiceStatusBadge, ItemStatusBadge, PlanStatusBadge } from '@/components/StatusBadge'
import { AppointmentDialog, type AppointmentDraft } from '@/features/appointments/AppointmentDialog'
import { PlanFormDialog } from './PlanFormDialog'

const nextStatuses: Partial<Record<TreatmentPlanStatus, { to: TreatmentPlanStatus; key: string }[]>> = {
  Draft: [{ to: 'Presented', key: 'plans.presentAction' }, { to: 'Accepted', key: 'plans.accept' }],
  Presented: [{ to: 'Accepted', key: 'plans.accept' }],
  InProgress: [{ to: 'PartiallyCompleted', key: 'plans.pause' }],
  PartiallyCompleted: [{ to: 'InProgress', key: 'plans.resume' }],
  Rejected: [{ to: 'Presented', key: 'plans.presentAction' }],
}

export default function PlanDetailPage() {
  const { id = '' } = useParams()
  const { t } = useTranslation()
  const { can } = useAuth()
  const navigate = useNavigate()
  const qc = useQueryClient()
  const canManage = can(Permissions.TreatmentManage)
  const [editing, setEditing] = useState(false)
  const [rejecting, setRejecting] = useState(false)
  const [reason, setReason] = useState('')
  const [completing, setCompleting] = useState<string | null>(null)
  const [cancelItem, setCancelItem] = useState<string | null>(null)
  const [booking, setBooking] = useState<AppointmentDraft | null>(null)
  const [itemNotes, setItemNotes] = useState('')

  const q = useQuery({ queryKey: ['plan', id], queryFn: () => api.get<TreatmentPlanDetail>(`/treatment-plans/${id}`) })
  const refresh = () => {
    for (const k of ['plan', 'plans', 'patient', 'recovery', 'dashboard', 'chart', 'treatments']) void qc.invalidateQueries({ queryKey: [k] })
  }

  const status = useMutation({
    mutationFn: (v: { status: TreatmentPlanStatus; reason?: string }) => api.patch(`/treatment-plans/${id}/status`, v),
    onSuccess: () => { toast.success(t('common.updated')); setRejecting(false); refresh() },
    onError: (e) => toast.error(errorMessage(e)),
  })
  const complete = useMutation({
    mutationFn: (itemId: string) => api.post(`/treatment-plans/${id}/items/${itemId}/complete`, { notes: itemNotes || null }),
    onSuccess: () => { toast.success(t('common.updated')); setCompleting(null); setItemNotes(''); refresh() },
    onError: (e) => toast.error(errorMessage(e)),
  })
  const cancel = useMutation({
    mutationFn: (itemId: string) => api.post(`/treatment-plans/${id}/items/${itemId}/cancel`),
    onSuccess: () => { setCancelItem(null); refresh() },
    onError: (e) => toast.error(errorMessage(e)),
  })
  const invoice = useMutation({
    mutationFn: (all: boolean) => api.post<{ id: string }>(`/treatment-plans/${id}/invoice`, { itemIds: null, includePlannedItems: all }),
    onSuccess: (r) => { toast.success(t('common.created')); refresh(); navigate(`/invoices/${r.id}`) },
    onError: (e) => toast.error(errorMessage(e)),
  })

  if (q.error) return <ErrorState error={q.error} onRetry={() => void q.refetch()} />
  if (q.isLoading || !q.data) return <div className="space-y-4"><Skeleton className="h-32" /><Skeleton className="h-96" /></div>

  const plan = q.data
  const s = plan.summary
  const closed = s.status === 'Completed' || s.status === 'Rejected'
  const hasUninvoicedPlanned = plan.items.some((i) => !i.isInvoiced && i.status !== 'Cancelled')
  const item = plan.items.find((i) => i.id === completing)

  return (
    <div className="space-y-6">
      <Link to="/treatment-plans" className="inline-flex items-center gap-1.5 text-sm text-ink-3 hover:text-ink"><ArrowLeft className="size-4 rtl:rotate-180" />{t('plans.title')}</Link>

      <Card className="p-5">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div>
            <div className="flex flex-wrap items-center gap-2">
              <h1 className="text-xl font-semibold text-ink">{s.title}</h1>
              <PlanStatusBadge status={s.status} />
            </div>
            <p className="mt-1 text-sm text-ink-3">
              {s.planNumber} · <Link className="text-primary-ink hover:underline" to={`/patients/${s.patientId}`}>{displayName(s.patientName, s.patientNameAr)}</Link> · {s.dentistName} · {date(s.createdAt)}
            </p>
            {plan.rejectionReason && <p className="mt-2 text-sm text-critical-ink">{t('plans.rejectReason')}: {plan.rejectionReason}</p>}
          </div>
          <div className="flex flex-wrap gap-2">
            {canManage && !closed && <Button variant="outline" icon={<Pencil className="size-4" />} onClick={() => setEditing(true)}>{t('common.edit')}</Button>}
            {can(Permissions.AppointmentsManage) && !closed && (
              <Button variant="outline" icon={<CalendarPlus className="size-4" />} onClick={() => setBooking({
                patient: { id: s.patientId, fullName: s.patientName, fullNameAr: s.patientNameAr, phone: s.patientPhone }, dentistId: s.dentistId,
                treatment: plan.items.find((i) => i.status === 'Planned')?.name ?? s.title, treatmentPlanId: s.id,
              })}>{t('patients.bookAppointment')}</Button>
            )}
            {canManage && nextStatuses[s.status]?.map((n) => (
              <Button key={n.to} variant={n.to === 'Accepted' ? 'primary' : 'outline'} loading={status.isPending && status.variables?.status === n.to}
                onClick={() => status.mutate({ status: n.to })}>{t(n.key as 'plans.accept')}</Button>
            ))}
            {canManage && !closed && plan.items.every((i) => i.status !== 'Planned' && i.status !== 'Scheduled') && plan.items.some((i) => i.status === 'Completed') && (
              <Button icon={<Check className="size-4" />} onClick={() => status.mutate({ status: 'Completed' })}>{t('plans.close')}</Button>
            )}
            {canManage && !closed && <Button variant="ghost" onClick={() => setRejecting(true)}>{t('plans.reject')}</Button>}
          </div>
        </div>
      </Card>

      <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        <StatCard label={t('common.total')} value={money(s.totalAmount)} hint={plan.discountAmount > 0 ? `${t('plans.planDiscount')}: ${money(plan.discountAmount)}` : undefined} />
        <StatCard label={t('plans.paid')} value={money(s.paidAmount)} tone="good" />
        <StatCard label={t('plans.remaining')} value={money(s.remainingAmount)} tone="warning" />
        <StatCard label={t('plans.completion')} value={`${s.completionPercent}%`} hint={<ProgressBar value={s.completionPercent} className="mt-1" />} />
      </div>

      <div className="grid gap-6 xl:grid-cols-[1fr_360px]">
        <Card>
          <CardHeader
            title={t('plans.items')}
            actions={can(Permissions.InvoicesManage) && (
              <>
                {plan.uninvoicedCompletedValue > 0 && (
                  <Button size="sm" icon={<FileText className="size-3.5" />} loading={invoice.isPending && invoice.variables === false} onClick={() => invoice.mutate(false)}>
                    {t('plans.invoiceCompleted')} ({money(plan.uninvoicedCompletedValue)})
                  </Button>
                )}
                {hasUninvoicedPlanned && !closed && (
                  <Button size="sm" variant="outline" loading={invoice.isPending && invoice.variables === true} onClick={() => invoice.mutate(true)}>{t('plans.invoiceAll')}</Button>
                )}
              </>
            )}
          />
          <Table>
            <THead>
              <tr><Th>#</Th><Th>{t('plans.procedure')}</Th><Th>{t('common.tooth')}</Th><Th className="text-end">{t('common.total')}</Th><Th>{t('common.status')}</Th><Th /></tr>
            </THead>
            <tbody>
              {plan.items.map((i) => (
                <Tr key={i.id} className={i.status === 'Cancelled' ? 'opacity-50' : undefined}>
                  <Td className="tabular text-ink-3">{i.sequence}</Td>
                  <Td>
                    <p className="font-medium text-ink">{i.name}</p>
                    <p className="text-xs text-ink-3">
                      {i.quantity > 1 && `${i.quantity} × ${money(i.unitPrice)} · `}{i.discount > 0 && `−${money(i.discount)} · `}
                      {i.completedAt && `${t('plans.performedAt')} ${date(i.completedAt)}`}{i.isInvoiced && ` · ${t('billing.invoice')}`}
                    </p>
                  </Td>
                  <Td className="tabular">{i.toothNumber ?? '—'}{i.surfaces && <span className="ms-1 text-xs text-ink-3">{i.surfaces}</span>}</Td>
                  <Td className="tabular text-end">{money(i.total)}</Td>
                  <Td><ItemStatusBadge status={i.status} /></Td>
                  <Td className="text-end">
                    {canManage && !closed && (i.status === 'Planned' || i.status === 'Scheduled') && s.status !== 'Draft' && (
                      <div className="flex justify-end gap-1">
                        <Button size="sm" icon={<Check className="size-3.5" />} onClick={() => setCompleting(i.id)}>{t('plans.completeItem')}</Button>
                        <Button size="icon" variant="ghost" className="size-8" onClick={() => setCancelItem(i.id)} aria-label={t('plans.cancelItem')}><X className="size-3.5" /></Button>
                      </div>
                    )}
                  </Td>
                </Tr>
              ))}
            </tbody>
          </Table>
          {s.status === 'Draft' && canManage && <p className="px-5 py-3 text-xs text-ink-3">{t('plans.present')}</p>}
        </Card>

        <div className="space-y-6">
          <Card>
            <CardHeader title={t('common.details')} />
            <div className="divide-y divide-line px-5 pb-3">
              <KeyValue label={t('enum.TreatmentPlanStatus.Presented')} value={s.presentedAt ? date(s.presentedAt) : null} />
              <KeyValue label={t('enum.TreatmentPlanStatus.Accepted')} value={plan.acceptedAt ? date(plan.acceptedAt) : null} />
              <KeyValue label={t('reports.started')} value={plan.startedAt ? date(plan.startedAt) : null} />
              <KeyValue label={t('enum.TreatmentPlanStatus.Completed')} value={plan.completedAt ? date(plan.completedAt) : null} />
              <KeyValue label={t('plans.nextAppointment')} value={s.nextAppointmentAt ? dateTime(s.nextAppointmentAt) : null} />
              {plan.notes && <p className="py-2 text-sm text-ink-2">{plan.notes}</p>}
            </div>
          </Card>
          <Card>
            <CardHeader title={t('plans.linkedInvoices')} />
            <div className="px-2 pb-3">
              {plan.invoices.length === 0 ? <p className="px-3 text-sm text-ink-3">—</p> : plan.invoices.map((inv) => (
                <Link key={inv.id} to={`/invoices/${inv.id}`} className="flex items-center justify-between gap-2 rounded-lg px-3 py-2 hover:bg-surface-2">
                  <span><span className="text-sm font-medium text-ink">{inv.invoiceNumber}</span><span className="ms-2 text-xs text-ink-3">{date(inv.issueDate)}</span></span>
                  <span className="flex items-center gap-2"><span className="tabular text-sm">{money(inv.total)}</span><InvoiceStatusBadge status={inv.status} /></span>
                </Link>
              ))}
            </div>
          </Card>
          <Card>
            <CardHeader title={t('plans.linkedAppointments')} />
            <div className="px-2 pb-3">
              {plan.appointments.length === 0 ? <p className="px-3 text-sm text-ink-3">—</p> : plan.appointments.map((a) => (
                <div key={a.id} className="flex items-center justify-between gap-2 px-3 py-2">
                  <span><span className="text-sm text-ink">{a.treatment}</span><span className="block text-xs text-ink-3">{dateTime(a.startTime)} · {a.dentistName}</span></span>
                  <AppointmentStatusBadge status={a.status} />
                </div>
              ))}
            </div>
          </Card>
        </div>
      </div>

      <PlanFormDialog open={editing} onClose={() => setEditing(false)} plan={plan} />
      <AppointmentDialog open={!!booking} draft={booking ?? {}} onClose={() => setBooking(null)} />
      <Dialog open={!!completing} onClose={() => setCompleting(null)} title={`${t('plans.completeItem')}: ${item?.name ?? ''}`} size="sm"
        footer={<><Button variant="outline" onClick={() => setCompleting(null)}>{t('common.cancel')}</Button><Button loading={complete.isPending} onClick={() => completing && complete.mutate(completing)}>{t('common.confirm')}</Button></>}>
        <Textarea rows={3} value={itemNotes} onChange={(e) => setItemNotes(e.target.value)} placeholder={t('common.notes')} maxLength={2000} />
      </Dialog>
      <ConfirmDialog open={!!cancelItem} onClose={() => setCancelItem(null)} onConfirm={() => cancelItem && cancel.mutate(cancelItem)} danger loading={cancel.isPending}
        title={t('plans.cancelItem')} body={t('common.confirmDelete')} />
      <ConfirmDialog open={rejecting} onClose={() => setRejecting(false)} onConfirm={() => status.mutate({ status: 'Rejected', reason })} danger loading={status.isPending}
        title={t('plans.reject')}>
        <Textarea className="mt-3" rows={2} value={reason} onChange={(e) => setReason(e.target.value)} placeholder={t('plans.rejectReason')} maxLength={500} />
      </ConfirmDialog>
    </div>
  )
}
