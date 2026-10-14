import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { Sparkles } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { clinicLocalToUtc, isoDateInClinic, money, date } from '@/lib/format'
import { useProcedures } from '@/lib/queries'
import { Permissions } from '@/lib/utils'
import type { InvoiceListItem, Paged, PaymentMethod } from '@/lib/types'
import { useAuth } from '@/auth/AuthProvider'
import { Dialog } from '@/components/ui/Dialog'
import { Button } from '@/components/ui/Button'
import { Field, Input, Select, Textarea } from '@/components/ui/primitives'
import { PatientPicker, type PickedPatient } from '@/components/PatientPicker'

const methods: PaymentMethod[] = ['Cash', 'Card', 'CliQ', 'BankTransfer', 'Insurance', 'Other']
/** Value of the invoice select that means "this payment is for something not invoiced yet". */
const NEW_CHARGE = 'new'

/**
 * Records a payment against one invoice, spreads it over the patient's oldest open invoices, or, when the
 * payment is for something not invoiced yet, creates the invoice together with the payment.
 */
export function RecordPaymentDialog({ open, onClose, patient: initialPatient, invoiceId: initialInvoice }: {
  open: boolean; onClose: () => void; patient?: PickedPatient | null; invoiceId?: string
}) {
  const { t, i18n } = useTranslation()
  const { can } = useAuth()
  const qc = useQueryClient()
  const canInvoice = can(Permissions.InvoicesManage)
  const [patient, setPatient] = useState<PickedPatient | null>(null)
  const [invoiceId, setInvoiceId] = useState('')
  const [amount, setAmount] = useState('')
  const [method, setMethod] = useState<PaymentMethod>('Cash')
  const [paidOn, setPaidOn] = useState(isoDateInClinic())
  const [reference, setReference] = useState('')
  const [notes, setNotes] = useState('')
  const [procedureId, setProcedureId] = useState('')
  const [description, setDescription] = useState('')
  const [chargeTotal, setChargeTotal] = useState('')
  const [error, setError] = useState<string | null>(null)

  const resetCharge = () => { setProcedureId(''); setDescription(''); setChargeTotal('') }

  useEffect(() => {
    if (!open) return
    setPatient(initialPatient ?? null)
    setInvoiceId(initialInvoice ?? '')
    setAmount('')
    setMethod('Cash')
    setPaidOn(isoDateInClinic())
    setReference('')
    setNotes('')
    resetCharge()
    setError(null)
  }, [open, initialPatient, initialInvoice])

  const invoices = useQuery({
    queryKey: ['open-invoices', patient?.id],
    queryFn: () => api.get<Paged<InvoiceListItem>>('/invoices', { patientId: patient!.id, openOnly: true, pageSize: 50 }),
    enabled: open && !!patient,
  })
  const procedures = useProcedures()
  const open_ = invoices.data?.items ?? []
  const outstanding = open_.reduce((s, i) => s + i.balance, 0)
  const selected = open_.find((i) => i.id === invoiceId)
  // With nothing outstanding, the only thing a payment can be for is a new charge.
  const charging = invoiceId === NEW_CHARGE || (!!invoices.data && open_.length === 0)
  // Without the invoicing permission a new charge is always paid in full, so the charge is the amount.
  const chargeValue = Number(canInvoice ? chargeTotal : amount)
  const maxAmount = charging ? chargeValue : selected ? selected.balance : outstanding

  useEffect(() => {
    if (invoices.data && !amount && !charging) setAmount(String(maxAmount || ''))
  }, [invoices.data, invoiceId]) // intentionally only when the invoice choice changes

  const pickProcedure = (id: string) => {
    setProcedureId(id)
    const p = procedures.data?.find((x) => x.id === id)
    if (!p) return
    setDescription(i18n.language === 'ar' && p.nameAr ? p.nameAr : p.name)
    setChargeTotal(String(p.defaultPrice))
    setAmount(String(p.defaultPrice))
  }

  const save = useMutation({
    mutationFn: () => api.post<{ receiptNumbers: string[]; recoveredAmount: number }>('/payments', {
      patientId: patient!.id, invoiceId: charging ? null : invoiceId || null, amount: Number(amount), method,
      paidAt: paidOn === isoDateInClinic() ? null : clinicLocalToUtc(`${paidOn}T12:00`),
      reference: reference || null, notes: notes || null,
      newCharge: charging ? { description: description.trim(), procedureId: procedureId || null, total: chargeValue, dentistId: null } : null,
    }),
    onSuccess: (r) => {
      toast.success(t('billing.paymentRecorded'), {
        description: [t('billing.receiptNo', { numbers: r.receiptNumbers.join(', ') }), r.recoveredAmount > 0 ? `✨ ${t('billing.recovered')}: ${money(r.recoveredAmount)}` : '']
          .filter(Boolean).join(' · '),
      })
      for (const k of ['invoices', 'invoice', 'payments', 'patient', 'dashboard', 'recovery', 'open-invoices', 'plan', 'plans', 'front-desk']) void qc.invalidateQueries({ queryKey: [k] })
      onClose()
    },
    onError: (e) => setError(errorMessage(e)),
  })

  const submit = () => {
    const value = Number(amount)
    if (!patient) return setError(t('common.required'))
    if (charging && !description.trim()) return setError(t('billing.chargeForRequired'))
    if (!value || value <= 0) return setError(t('billing.amountRequired'))
    if (charging && !(chargeValue > 0)) return setError(t('billing.amountRequired'))
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
          <Button onClick={submit} loading={save.isPending} disabled={!patient || !invoices.data}>{t('common.save')}</Button>
        </>
      }
    >
      <div className="space-y-4">
        <Field label={t('common.patient')} required>
          <PatientPicker value={patient} onChange={(p) => { setPatient(p); setInvoiceId(''); setAmount(''); resetCharge() }} disabled={!!initialPatient} />
        </Field>
        {patient && (
          invoices.isLoading ? <p className="text-sm text-ink-3">{t('common.loading')}</p> : (
            <>
              {open_.length === 0
                ? <p className="rounded-lg bg-surface-2 p-3 text-sm leading-relaxed text-ink-2">{t('billing.nothingOutstanding')}</p>
                : (
                  <Field label={t('billing.invoice')}>
                    <Select value={invoiceId} onChange={(e) => { setInvoiceId(e.target.value); setAmount(''); resetCharge() }}>
                      <option value="">{t('billing.payAll')} ({money(outstanding)})</option>
                      {open_.map((i) => (
                        <option key={i.id} value={i.id}>{i.invoiceNumber} · {date(i.issueDate)} · {money(i.balance)}{i.isOverdue ? ` · ${t('billing.overdue')}` : ''}</option>
                      ))}
                      <option value={NEW_CHARGE}>{t('billing.newCharge')}</option>
                    </Select>
                  </Field>
                )}
              {charging && (
                <>
                  <Field label={t('billing.chargeProcedure')} hint={t('common.optional')}>
                    <Select value={procedureId} onChange={(e) => pickProcedure(e.target.value)}>
                      <option value="">—</option>
                      {(procedures.data ?? []).filter((p) => p.isActive).map((p) => (
                        <option key={p.id} value={p.id}>{i18n.language === 'ar' && p.nameAr ? p.nameAr : p.name} · {money(p.defaultPrice)}</option>
                      ))}
                    </Select>
                  </Field>
                  <Field label={t('billing.chargeFor')} required>
                    <Input value={description} onChange={(e) => setDescription(e.target.value)} maxLength={200} />
                  </Field>
                  {canInvoice && (
                    <Field label={t('billing.chargeTotal')} required>
                      <Input type="number" inputMode="decimal" min={0} step="0.001" value={chargeTotal}
                        onChange={(e) => { setChargeTotal(e.target.value); setAmount(e.target.value) }} />
                    </Field>
                  )}
                </>
              )}
              <div className="grid gap-4 sm:grid-cols-2">
                <Field label={charging ? t('billing.amountPaid') : t('common.amount')} required hint={charging ? undefined : `max ${money(maxAmount)}`}>
                  <Input type="number" inputMode="decimal" min={0} step="0.001" value={amount} onChange={(e) => setAmount(e.target.value)} />
                </Field>
                <Field label={t('common.method')}>
                  <Select value={method} onChange={(e) => setMethod(e.target.value as PaymentMethod)}>
                    {methods.map((m) => <option key={m} value={m}>{t(`enum.PaymentMethod.${m}`)}</option>)}
                  </Select>
                </Field>
                <Field label={t('billing.paidOn')}>
                  <Input type="date" max={isoDateInClinic()} value={paidOn} onChange={(e) => setPaidOn(e.target.value || isoDateInClinic())} />
                </Field>
                <Field label={t('common.reference')} hint={t('common.optional')}><Input value={reference} onChange={(e) => setReference(e.target.value)} maxLength={100} /></Field>
              </div>
              <Field label={t('common.notes')}><Textarea rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} maxLength={1000} /></Field>
              {charging
                ? <p className="text-xs leading-relaxed text-ink-3">{t(canInvoice ? 'billing.chargePartHint' : 'billing.chargeFullHint')}</p>
                : <p className="flex gap-1.5 text-xs text-ink-3"><Sparkles className="size-3.5 shrink-0 text-recovery" />{t('billing.recoveryNote')}</p>}
            </>
          )
        )}
        {error && <p role="alert" className="rounded-lg bg-critical-soft px-3 py-2 text-sm text-critical-ink">{error}</p>}
      </div>
    </Dialog>
  )
}
