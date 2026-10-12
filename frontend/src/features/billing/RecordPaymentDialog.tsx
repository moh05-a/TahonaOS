import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { Sparkles } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { money, date } from '@/lib/format'
import type { InvoiceListItem, Paged, PaymentMethod } from '@/lib/types'
import { Dialog } from '@/components/ui/Dialog'
import { Button } from '@/components/ui/Button'
import { Field, Input, Select, Textarea } from '@/components/ui/primitives'
import { PatientPicker, type PickedPatient } from '@/components/PatientPicker'

const methods: PaymentMethod[] = ['Cash', 'Card', 'CliQ', 'BankTransfer', 'Insurance', 'Other']

/** Records a payment against one invoice, or spreads it over the patient's oldest open invoices. */
export function RecordPaymentDialog({ open, onClose, patient: initialPatient, invoiceId: initialInvoice }: {
  open: boolean; onClose: () => void; patient?: PickedPatient | null; invoiceId?: string
}) {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const [patient, setPatient] = useState<PickedPatient | null>(null)
  const [invoiceId, setInvoiceId] = useState('')
  const [amount, setAmount] = useState('')
  const [method, setMethod] = useState<PaymentMethod>('Cash')
  const [reference, setReference] = useState('')
  const [notes, setNotes] = useState('')
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    setPatient(initialPatient ?? null)
    setInvoiceId(initialInvoice ?? '')
    setAmount('')
    setMethod('Cash')
    setReference('')
    setNotes('')
    setError(null)
  }, [open, initialPatient, initialInvoice])

  const invoices = useQuery({
    queryKey: ['open-invoices', patient?.id],
    queryFn: () => api.get<Paged<InvoiceListItem>>('/invoices', { patientId: patient!.id, openOnly: true, pageSize: 50 }),
    enabled: open && !!patient,
  })
  const open_ = invoices.data?.items ?? []
  const selected = open_.find((i) => i.id === invoiceId)
  const maxAmount = selected ? selected.balance : open_.reduce((s, i) => s + i.balance, 0)

  useEffect(() => {
    if (invoices.data && !amount) setAmount(String(maxAmount || ''))
  }, [invoices.data, invoiceId]) // intentionally only when the invoice choice changes

  const save = useMutation({
    mutationFn: () => api.post<{ receiptNumbers: string[]; recoveredAmount: number }>('/payments', {
      patientId: patient!.id, invoiceId: invoiceId || null, amount: Number(amount), method, reference: reference || null, notes: notes || null,
    }),
    onSuccess: (r) => {
      toast.success(t('billing.paymentRecorded'), {
        description: [t('billing.receiptNo', { numbers: r.receiptNumbers.join(', ') }), r.recoveredAmount > 0 ? `✨ ${t('billing.recovered')}: ${money(r.recoveredAmount)}` : '']
          .filter(Boolean).join(' · '),
      })
      for (const k of ['invoices', 'invoice', 'payments', 'patient', 'dashboard', 'recovery', 'open-invoices', 'plan', 'plans']) void qc.invalidateQueries({ queryKey: [k] })
      onClose()
    },
    onError: (e) => setError(errorMessage(e)),
  })

  const submit = () => {
    const value = Number(amount)
    if (!patient) return setError(t('common.required'))
    if (!value || value <= 0) return setError(t('common.required'))
    if (value > maxAmount + 0.0005) return setError(`≤ ${money(maxAmount)}`)
    setError(null)
    save.mutate()
  }

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title={t('billing.recordPayment')}
      footer={
        <>
          <Button variant="outline" onClick={onClose}>{t('common.cancel')}</Button>
          <Button onClick={submit} loading={save.isPending} disabled={!patient || open_.length === 0}>{t('common.save')}</Button>
        </>
      }
    >
      <div className="space-y-4">
        <Field label={t('common.patient')} required>
          <PatientPicker value={patient} onChange={(p) => { setPatient(p); setInvoiceId(''); setAmount('') }} disabled={!!initialPatient} />
        </Field>
        {patient && (
          invoices.isLoading ? <p className="text-sm text-ink-3">{t('common.loading')}</p>
            : open_.length === 0 ? <p className="rounded-lg bg-surface-2 p-3 text-sm text-ink-2">{t('billing.empty')} — {t('billing.balance')}: {money(0)}</p>
            : (
              <>
                <Field label={t('billing.invoice')}>
                  <Select value={invoiceId} onChange={(e) => { setInvoiceId(e.target.value); setAmount('') }}>
                    <option value="">{t('billing.payAll')} ({money(open_.reduce((s, i) => s + i.balance, 0))})</option>
                    {open_.map((i) => (
                      <option key={i.id} value={i.id}>{i.invoiceNumber} · {date(i.issueDate)} · {money(i.balance)}{i.isOverdue ? ` · ${t('billing.overdue')}` : ''}</option>
                    ))}
                  </Select>
                </Field>
                <div className="grid gap-4 sm:grid-cols-2">
                  <Field label={t('common.amount')} required hint={`max ${money(maxAmount)}`}>
                    <Input type="number" inputMode="decimal" min={0} step="0.001" value={amount} onChange={(e) => setAmount(e.target.value)} />
                  </Field>
                  <Field label={t('common.method')}>
                    <Select value={method} onChange={(e) => setMethod(e.target.value as PaymentMethod)}>
                      {methods.map((m) => <option key={m} value={m}>{t(`enum.PaymentMethod.${m}`)}</option>)}
                    </Select>
                  </Field>
                </div>
                <Field label={t('common.reference')} hint={t('common.optional')}><Input value={reference} onChange={(e) => setReference(e.target.value)} maxLength={100} /></Field>
                <Field label={t('common.notes')}><Textarea rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} maxLength={1000} /></Field>
                <p className="flex gap-1.5 text-xs text-ink-3"><Sparkles className="size-3.5 shrink-0 text-recovery" />{t('billing.recoveryNote')}</p>
              </>
            )
        )}
        {error && <p role="alert" className="rounded-lg bg-critical-soft px-3 py-2 text-sm text-critical-ink">{error}</p>}
      </div>
    </Dialog>
  )
}
