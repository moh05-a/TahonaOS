import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ArrowLeft, Ban, CreditCard, Printer, RotateCcw, Send, Sparkles } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { date, dateTime, money } from '@/lib/format'
import { displayName, Permissions } from '@/lib/utils'
import type { ClinicProfile, InvoiceDetail } from '@/lib/types'
import { useAuth } from '@/auth/AuthProvider'
import { Badge, Card, CardHeader, ErrorState, Field, Input, Skeleton, Textarea } from '@/components/ui/primitives'
import { Button } from '@/components/ui/Button'
import { ConfirmDialog, Dialog } from '@/components/ui/Dialog'
import { Table, THead, Td, Th, Tr } from '@/components/ui/Table'
import { InvoiceStatusBadge } from '@/components/StatusBadge'
import { RecordPaymentDialog } from './RecordPaymentDialog'

export default function InvoiceDetailPage() {
  const { id = '' } = useParams()
  const { t } = useTranslation()
  const { can } = useAuth()
  const qc = useQueryClient()
  const [paying, setPaying] = useState(false)
  const [voiding, setVoiding] = useState(false)
  const [voidReason, setVoidReason] = useState('')
  const [refund, setRefund] = useState<{ id: string; max: number } | null>(null)
  const [refundAmount, setRefundAmount] = useState('')
  const [refundReason, setRefundReason] = useState('')

  const q = useQuery({ queryKey: ['invoice', id], queryFn: () => api.get<InvoiceDetail>(`/invoices/${id}`) })
  const clinic = useQuery({ queryKey: ['clinic-profile'], queryFn: () => api.get<ClinicProfile>('/settings/clinic'), staleTime: 300_000 })
  const refresh = () => { for (const k of ['invoice', 'invoices', 'payments', 'patient', 'dashboard', 'recovery']) void qc.invalidateQueries({ queryKey: [k] }) }

  const issue = useMutation({
    mutationFn: () => api.post(`/invoices/${id}/issue`),
    onSuccess: () => { toast.success(t('common.updated')); refresh() },
    onError: (e) => toast.error(errorMessage(e)),
  })
  const voidInv = useMutation({
    mutationFn: () => api.post(`/invoices/${id}/void`, { reason: voidReason }),
    onSuccess: () => { toast.success(t('common.updated')); setVoiding(false); refresh() },
    onError: (e) => toast.error(errorMessage(e)),
  })
  const doRefund = useMutation({
    mutationFn: () => api.post(`/payments/${refund!.id}/refund`, { amount: Number(refundAmount), reason: refundReason }),
    onSuccess: () => { toast.success(t('common.saved')); setRefund(null); refresh() },
    onError: (e) => toast.error(errorMessage(e)),
  })

  if (q.error) return <ErrorState error={q.error} onRetry={() => void q.refetch()} />
  if (q.isLoading || !q.data) return <Skeleton className="h-[600px]" />

  const inv = q.data
  const s = inv.summary

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3 print:hidden">
        <Link to="/invoices" className="inline-flex items-center gap-1.5 text-sm text-ink-3 hover:text-ink"><ArrowLeft className="size-4 rtl:rotate-180" />{t('billing.invoices')}</Link>
        <div className="flex flex-wrap gap-2">
          <Button variant="outline" icon={<Printer className="size-4" />} onClick={() => window.print()}>{t('common.print')}</Button>
          {s.status === 'Draft' && can(Permissions.InvoicesManage) && <Button icon={<Send className="size-4" />} loading={issue.isPending} onClick={() => issue.mutate()}>{t('billing.issue')}</Button>}
          {s.balance > 0 && ['Issued', 'PartiallyPaid'].includes(s.status) && can(Permissions.PaymentsManage) && (
            <Button icon={<CreditCard className="size-4" />} onClick={() => setPaying(true)}>{t('billing.recordPayment')}</Button>
          )}
          {!['Void', 'Paid', 'Refunded'].includes(s.status) && s.amountPaid <= 0 && can(Permissions.InvoicesManage) && (
            <Button variant="ghost" icon={<Ban className="size-4" />} onClick={() => setVoiding(true)}>{t('billing.void')}</Button>
          )}
        </div>
      </div>

      <Card className="mx-auto max-w-4xl p-6 sm:p-10 print:border-0 print:shadow-none">
        <div className="flex flex-wrap items-start justify-between gap-6">
          <div>
            <p className="text-lg font-semibold text-ink">{clinic.data?.name}</p>
            {clinic.data?.nameAr && <p className="text-sm text-ink-2" dir="rtl">{clinic.data.nameAr}</p>}
            <p className="mt-1 text-xs text-ink-3">{[clinic.data?.address, clinic.data?.city].filter(Boolean).join(', ')}</p>
            <p className="ltr text-xs text-ink-3">{clinic.data?.phone}</p>
          </div>
          <div className="text-end">
            <p className="text-2xl font-semibold tracking-tight text-ink">{t('billing.invoice')}</p>
            <p className="text-sm font-medium text-ink-2">{s.invoiceNumber}</p>
            <div className="mt-2"><InvoiceStatusBadge status={s.status} overdue={s.isOverdue} /></div>
          </div>
        </div>

        <div className="mt-8 grid gap-6 sm:grid-cols-2">
          <div>
            <p className="text-xs font-medium tracking-wide text-ink-3 uppercase">{t('common.patient')}</p>
            <Link to={`/patients/${s.patientId}`} className="mt-1 block font-medium text-ink hover:underline">{displayName(s.patientName, s.patientNameAr)}</Link>
            <p className="ltr text-sm text-ink-2">{s.patientPhone}</p>
            {inv.dentistName && <p className="text-sm text-ink-3">{inv.dentistName}</p>}
          </div>
          <div className="grid grid-cols-2 gap-2 text-sm sm:text-end">
            <span className="text-ink-3">{t('billing.issueDate')}</span><span className="text-ink">{date(s.issueDate)}</span>
            <span className="text-ink-3">{t('billing.dueDate')}</span><span className="text-ink">{date(s.dueDate)}</span>
            {s.planNumber && <><span className="text-ink-3">{t('nav.treatmentPlans')}</span><Link className="text-primary-ink" to={`/treatment-plans/${s.treatmentPlanId}`}>{s.planNumber}</Link></>}
          </div>
        </div>

        <div className="mt-8 overflow-hidden rounded-xl border border-line">
          <Table>
            <THead><tr><Th>{t('common.description')}</Th><Th className="text-end">{t('plans.qty')}</Th><Th className="text-end">{t('plans.unitPrice')}</Th><Th className="text-end">{t('plans.discount')}</Th><Th className="text-end">{t('common.total')}</Th></tr></THead>
            <tbody>
              {inv.items.map((i) => (
                <Tr key={i.id}>
                  <Td>{i.description}</Td>
                  <Td className="tabular text-end">{i.quantity}</Td>
                  <Td className="tabular text-end">{money(i.unitPrice)}</Td>
                  <Td className="tabular text-end">{i.discount > 0 ? money(i.discount) : '—'}</Td>
                  <Td className="tabular text-end font-medium">{money(i.total)}</Td>
                </Tr>
              ))}
            </tbody>
          </Table>
        </div>

        <div className="mt-6 ms-auto w-full max-w-xs space-y-1.5 text-sm">
          <Row label={t('plans.subtotal')} value={money(inv.subtotal)} />
          {inv.discountAmount > 0 && <Row label={t('plans.discount')} value={`−${money(inv.discountAmount)}`} />}
          {inv.taxAmount > 0 && <Row label={t('billing.tax')} value={money(inv.taxAmount)} />}
          <div className="border-t border-line pt-1.5"><Row label={t('common.total')} value={money(s.total)} bold /></div>
          <Row label={t('billing.paid')} value={money(s.amountPaid)} />
          <div className="border-t border-line pt-1.5"><Row label={t('billing.balance')} value={money(s.balance)} bold danger={s.balance > 0} /></div>
        </div>
        {inv.notes && <p className="mt-6 text-sm whitespace-pre-wrap text-ink-2">{inv.notes}</p>}
      </Card>

      <Card className="mx-auto max-w-4xl print:hidden">
        <CardHeader title={t('billing.payments')} />
        {inv.payments.length === 0 ? <p className="px-5 pb-5 text-sm text-ink-3">—</p> : (
          <Table>
            <THead><tr><Th>{t('billing.receipt')}</Th><Th>{t('common.date')}</Th><Th>{t('common.method')}</Th><Th>{t('common.reference')}</Th><Th className="text-end">{t('common.amount')}</Th><Th /></tr></THead>
            <tbody>
              {inv.payments.map((p) => (
                <Tr key={p.id}>
                  <Td className="font-medium">{p.receiptNumber}{p.isRecovered && <Badge tone="recovery" className="ms-2"><Sparkles className="size-3" />{t('billing.recovered')}</Badge>}</Td>
                  <Td>{dateTime(p.paidAt)}{p.receivedBy && <span className="block text-xs text-ink-3">{p.receivedBy}</span>}</Td>
                  <Td>{t(`enum.PaymentType.${p.type}`)} · {t(`enum.PaymentMethod.${p.method}`)}</Td>
                  <Td className="text-ink-2">{p.reference ?? '—'}</Td>
                  <Td className={`tabular text-end font-medium ${p.type === 'Refund' ? 'text-critical-ink' : ''}`}>{p.type === 'Refund' ? '−' : ''}{money(p.amount)}</Td>
                  <Td className="text-end">
                    {p.type === 'Payment' && can(Permissions.PaymentsRefund) && (
                      <Button size="sm" variant="ghost" icon={<RotateCcw className="size-3.5" />} onClick={() => { setRefund({ id: p.id, max: p.amount }); setRefundAmount(String(p.amount)); setRefundReason('') }}>
                        {t('billing.refund')}
                      </Button>
                    )}
                  </Td>
                </Tr>
              ))}
            </tbody>
          </Table>
        )}
      </Card>

      <RecordPaymentDialog open={paying} onClose={() => setPaying(false)} patient={{ id: s.patientId, fullName: s.patientName, fullNameAr: s.patientNameAr, phone: s.patientPhone }} invoiceId={s.id} />
      <ConfirmDialog open={voiding} onClose={() => setVoiding(false)} onConfirm={() => voidInv.mutate()} danger loading={voidInv.isPending} title={t('billing.void')}>
        <Textarea className="mt-3" rows={2} value={voidReason} onChange={(e) => setVoidReason(e.target.value)} placeholder={t('billing.voidReason')} maxLength={500} />
      </ConfirmDialog>
      <Dialog open={!!refund} onClose={() => setRefund(null)} title={t('billing.refund')} size="sm"
        footer={<><Button variant="outline" onClick={() => setRefund(null)}>{t('common.cancel')}</Button>
          <Button variant="danger" loading={doRefund.isPending} disabled={!refundReason.trim() || !(Number(refundAmount) > 0) || Number(refundAmount) > (refund?.max ?? 0)}
            onClick={() => doRefund.mutate()}>{t('billing.refund')}</Button></>}>
        <div className="space-y-3">
          <Field label={t('common.amount')} hint={refund ? `max ${money(refund.max)}` : undefined}><Input type="number" min={0} step="0.001" value={refundAmount} onChange={(e) => setRefundAmount(e.target.value)} /></Field>
          <Field label={t('billing.refundReason')} required><Textarea rows={2} value={refundReason} onChange={(e) => setRefundReason(e.target.value)} maxLength={500} /></Field>
        </div>
      </Dialog>
    </div>
  )
}

function Row({ label, value, bold, danger }: { label: string; value: string; bold?: boolean; danger?: boolean }) {
  return (
    <div className="flex justify-between">
      <span className={bold ? 'font-medium text-ink' : 'text-ink-3'}>{label}</span>
      <span className={`tabular ${bold ? 'font-semibold' : ''} ${danger ? 'text-critical-ink' : 'text-ink'}`}>{value}</span>
    </div>
  )
}
