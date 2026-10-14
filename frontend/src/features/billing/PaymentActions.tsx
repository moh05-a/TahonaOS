import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { Ban, Pencil, RotateCcw } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { clinicLocalToUtc, money, utcToClinicLocal } from '@/lib/format'
import { Permissions } from '@/lib/utils'
import type { PaymentListItem, PaymentMethod } from '@/lib/types'
import { useAuth } from '@/auth/AuthProvider'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field, Input, Select, Textarea } from '@/components/ui/primitives'

const methods: PaymentMethod[] = ['Cash', 'Card', 'CliQ', 'BankTransfer', 'Insurance', 'Other']
const affected = ['payments', 'invoices', 'invoice', 'patient', 'dashboard', 'recovery', 'open-invoices', 'front-desk', 'reports']

function useRefreshBilling() {
  const qc = useQueryClient()
  return () => { for (const key of affected) void qc.invalidateQueries({ queryKey: [key] }) }
}

/** What staff can do with one recorded payment: correct its details, refund it, or cancel it as a mistake. */
export function PaymentRowActions({ payment }: { payment: PaymentListItem }) {
  const { t } = useTranslation()
  const { can } = useAuth()
  const [mode, setMode] = useState<'edit' | 'refund' | 'cancel' | null>(null)
  if (payment.isCancelled) return null

  const refundable = payment.type === 'Payment' ? payment.amount - payment.refundedAmount : 0
  const canEdit = can(Permissions.PaymentsManage)
  const canReverse = can(Permissions.PaymentsRefund) && payment.type === 'Payment'
  if (!canEdit && !canReverse) return null
  const close = () => setMode(null)

  return (
    <div className="flex justify-end gap-1">
      {canEdit && (
        <Button size="icon" variant="ghost" className="size-8" onClick={() => setMode('edit')} aria-label={t('billing.editPayment')} title={t('billing.editPayment')}>
          <Pencil className="size-3.5" />
        </Button>
      )}
      {canReverse && refundable > 0 && (
        <Button size="icon" variant="ghost" className="size-8" onClick={() => setMode('refund')} aria-label={t('billing.refund')} title={t('billing.refund')}>
          <RotateCcw className="size-3.5" />
        </Button>
      )}
      {canReverse && payment.refundedAmount <= 0 && (
        <Button size="icon" variant="ghost" className="size-8 text-critical-ink" onClick={() => setMode('cancel')} aria-label={t('billing.cancelPayment')} title={t('billing.cancelPayment')}>
          <Ban className="size-3.5" />
        </Button>
      )}
      {mode === 'edit' && <EditPaymentDialog payment={payment} onClose={close} />}
      {mode === 'refund' && <RefundPaymentDialog payment={payment} max={refundable} onClose={close} />}
      {mode === 'cancel' && <CancelPaymentDialog payment={payment} onClose={close} />}
    </div>
  )
}

function Summary({ payment }: { payment: PaymentListItem }) {
  return (
    <p className="rounded-lg bg-surface-2 px-3 py-2 text-sm text-ink-2">
      <span className="font-medium text-ink">{payment.receiptNumber}</span> · {payment.patientName} · <span className="tabular font-medium text-ink">{money(payment.amount)}</span>
    </p>
  )
}

function EditPaymentDialog({ payment, onClose }: { payment: PaymentListItem; onClose: () => void }) {
  const { t } = useTranslation()
  const refresh = useRefreshBilling()
  const [method, setMethod] = useState<PaymentMethod>(payment.method)
  const [paidAt, setPaidAt] = useState(utcToClinicLocal(payment.paidAt))
  const [reference, setReference] = useState(payment.reference ?? '')
  const [notes, setNotes] = useState(payment.notes ?? '')
  const [error, setError] = useState<string | null>(null)

  const save = useMutation({
    mutationFn: () => api.put(`/payments/${payment.id}`, { method, paidAt: clinicLocalToUtc(paidAt), reference: reference || null, notes: notes || null }),
    onSuccess: () => { toast.success(t('common.saved')); refresh(); onClose() },
    onError: (e) => setError(errorMessage(e)),
  })

  return (
    <Dialog open onClose={onClose} title={t('billing.editPayment')}
      footer={<><Button variant="outline" onClick={onClose}>{t('common.cancel')}</Button>
        <Button loading={save.isPending} disabled={!paidAt} onClick={() => save.mutate()}>{t('common.save')}</Button></>}>
      <div className="space-y-4">
        <Summary payment={payment} />
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t('common.method')}>
            <Select value={method} onChange={(e) => setMethod(e.target.value as PaymentMethod)}>
              {methods.map((m) => <option key={m} value={m}>{t(`enum.PaymentMethod.${m}`)}</option>)}
            </Select>
          </Field>
          <Field label={t('billing.paidOn')}>
            <Input type="datetime-local" value={paidAt} onChange={(e) => setPaidAt(e.target.value)} />
          </Field>
        </div>
        <Field label={t('common.reference')} hint={t('common.optional')}><Input value={reference} onChange={(e) => setReference(e.target.value)} maxLength={100} /></Field>
        <Field label={t('common.notes')}><Textarea rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} maxLength={1000} /></Field>
        <p className="text-xs leading-relaxed text-ink-3">{t('billing.editPaymentHint')}</p>
        {error && <p role="alert" className="rounded-lg bg-critical-soft px-3 py-2 text-sm text-critical-ink">{error}</p>}
      </div>
    </Dialog>
  )
}

function RefundPaymentDialog({ payment, max, onClose }: { payment: PaymentListItem; max: number; onClose: () => void }) {
  const { t } = useTranslation()
  const refresh = useRefreshBilling()
  const [amount, setAmount] = useState(String(max))
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)

  const refund = useMutation({
    mutationFn: () => api.post(`/payments/${payment.id}/refund`, { amount: Number(amount), reason }),
    onSuccess: () => { toast.success(t('common.saved')); refresh(); onClose() },
    onError: (e) => setError(errorMessage(e)),
  })
  const valid = reason.trim() && Number(amount) > 0 && Number(amount) <= max + 0.0005

  return (
    <Dialog open onClose={onClose} title={t('billing.refund')} size="sm"
      footer={<><Button variant="outline" onClick={onClose}>{t('common.cancel')}</Button>
        <Button variant="danger" loading={refund.isPending} disabled={!valid} onClick={() => refund.mutate()}>{t('billing.refund')}</Button></>}>
      <div className="space-y-4">
        <Summary payment={payment} />
        <Field label={t('common.amount')} hint={`max ${money(max)}`}>
          <Input type="number" inputMode="decimal" min={0} step="0.001" value={amount} onChange={(e) => setAmount(e.target.value)} />
        </Field>
        <Field label={t('billing.refundReason')} required><Textarea rows={2} value={reason} onChange={(e) => setReason(e.target.value)} maxLength={500} /></Field>
        {error && <p role="alert" className="rounded-lg bg-critical-soft px-3 py-2 text-sm text-critical-ink">{error}</p>}
      </div>
    </Dialog>
  )
}

function CancelPaymentDialog({ payment, onClose }: { payment: PaymentListItem; onClose: () => void }) {
  const { t } = useTranslation()
  const refresh = useRefreshBilling()
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)

  const cancel = useMutation({
    mutationFn: () => api.post(`/payments/${payment.id}/cancel`, { reason }),
    onSuccess: () => { toast.success(t('billing.paymentCancelled')); refresh(); onClose() },
    onError: (e) => setError(errorMessage(e)),
  })

  return (
    <Dialog open onClose={onClose} title={t('billing.cancelPaymentTitle')} size="sm"
      footer={<><Button variant="outline" onClick={onClose}>{t('common.back')}</Button>
        <Button variant="danger" loading={cancel.isPending} disabled={!reason.trim()} onClick={() => cancel.mutate()}>{t('billing.cancelPayment')}</Button></>}>
      <div className="space-y-4">
        <Summary payment={payment} />
        <p className="text-sm leading-relaxed text-ink-2">{t('billing.cancelPaymentBody')}</p>
        <Field label={t('billing.cancelReason')} required><Textarea rows={2} value={reason} onChange={(e) => setReason(e.target.value)} maxLength={500} /></Field>
        {error && <p role="alert" className="rounded-lg bg-critical-soft px-3 py-2 text-sm text-critical-ink">{error}</p>}
      </div>
    </Dialog>
  )
}
