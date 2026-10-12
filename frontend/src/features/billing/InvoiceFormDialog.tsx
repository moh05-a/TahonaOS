import { useEffect, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { Plus, Trash2 } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { money, isoDateInClinic, clinicLocalToUtc } from '@/lib/format'
import { useDentists, useProcedures } from '@/lib/queries'
import { Dialog } from '@/components/ui/Dialog'
import { Button } from '@/components/ui/Button'
import { Field, Input, Select, Textarea } from '@/components/ui/primitives'
import { PatientPicker, type PickedPatient } from '@/components/PatientPicker'

interface Line { procedureId: string; description: string; toothNumber: string; quantity: number; unitPrice: number; discount: number }
const newLine = (): Line => ({ procedureId: '', description: '', toothNumber: '', quantity: 1, unitPrice: 0, discount: 0 })

export function InvoiceFormDialog({ open, onClose, patient: initialPatient, onSaved }: {
  open: boolean; onClose: () => void; patient?: PickedPatient | null; onSaved?: (id: string) => void
}) {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const procedures = useProcedures()
  const dentists = useDentists()
  const [patient, setPatient] = useState<PickedPatient | null>(null)
  const [dentistId, setDentistId] = useState('')
  const [lines, setLines] = useState<Line[]>([newLine()])
  const [discount, setDiscount] = useState(0)
  const [dueDate, setDueDate] = useState('')
  const [notes, setNotes] = useState('')
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    setPatient(initialPatient ?? null)
    setDentistId('')
    setLines([newLine()])
    setDiscount(0)
    setDueDate('')
    setNotes('')
    setError(null)
  }, [open, initialPatient])

  const update = (i: number, patch: Partial<Line>) => setLines((ls) => ls.map((l, j) => (j === i ? { ...l, ...patch } : l)))
  const subtotal = lines.reduce((s, l) => s + Math.max(0, l.unitPrice * l.quantity - l.discount), 0)

  const save = useMutation({
    mutationFn: (draft: boolean) => api.post<{ id: string }>('/invoices', {
      patientId: patient!.id, dentistId: dentistId || null, treatmentPlanId: null,
      items: lines.map((l) => ({
        description: l.description.trim(), procedureId: l.procedureId || null, treatmentPlanItemId: null,
        toothNumber: l.toothNumber ? Number(l.toothNumber) : null, quantity: l.quantity, unitPrice: l.unitPrice, discount: l.discount,
      })),
      discountAmount: discount, dueDate: dueDate ? clinicLocalToUtc(`${dueDate}T23:59`) : null, notes: notes || null, saveAsDraft: draft,
    }),
    onSuccess: (r) => {
      toast.success(t('common.created'))
      void qc.invalidateQueries({ queryKey: ['invoices'] })
      void qc.invalidateQueries({ queryKey: ['patient'] })
      void qc.invalidateQueries({ queryKey: ['dashboard'] })
      onSaved?.(r.id)
      onClose()
    },
    onError: (e) => setError(errorMessage(e)),
  })

  const submit = (draft: boolean) => {
    if (!patient) return setError(`${t('common.patient')}: ${t('common.required')}`)
    if (lines.some((l) => !l.description.trim())) return setError(`${t('common.description')}: ${t('common.required')}`)
    if (lines.some((l) => l.discount > l.unitPrice * l.quantity)) return setError(t('plans.discount'))
    setError(null)
    save.mutate(draft)
  }

  return (
    <Dialog
      open={open}
      onClose={onClose}
      size="xl"
      title={t('billing.newInvoice')}
      footer={
        <>
          <span className="tabular me-auto text-sm text-ink-2">{t('common.total')}: <span className="font-semibold text-ink">{money(Math.max(0, subtotal - discount))}</span></span>
          <Button variant="outline" onClick={() => submit(true)} loading={save.isPending && save.variables === true}>{t('billing.draft')}</Button>
          <Button onClick={() => submit(false)} loading={save.isPending && save.variables === false}>{t('billing.issue')}</Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="grid gap-4 sm:grid-cols-3">
          <Field label={t('common.patient')} required className="sm:col-span-2"><PatientPicker value={patient} onChange={setPatient} disabled={!!initialPatient} /></Field>
          <Field label={t('common.dentist')}>
            <Select value={dentistId} onChange={(e) => setDentistId(e.target.value)}>
              <option value="">—</option>
              {dentists.data?.map((d) => <option key={d.id} value={d.id}>{d.fullName}</option>)}
            </Select>
          </Field>
        </div>

        <div className="rounded-xl border border-line">
          <div className="grid grid-cols-12 gap-2 border-b border-line bg-surface-2 px-3 py-2 text-xs font-medium text-ink-3">
            <span className="col-span-5">{t('common.description')}</span><span className="col-span-1">{t('common.tooth')}</span>
            <span className="col-span-1">{t('plans.qty')}</span><span className="col-span-2">{t('plans.unitPrice')}</span>
            <span className="col-span-2">{t('plans.discount')}</span><span />
          </div>
          {lines.map((l, i) => (
            <div key={i} className="grid grid-cols-12 items-center gap-2 border-b border-line px-3 py-2 last:border-0">
              <div className="col-span-5 space-y-1">
                <Select value={l.procedureId} onChange={(e) => {
                  const p = procedures.data?.find((x) => x.id === e.target.value)
                  update(i, { procedureId: e.target.value, description: p?.name ?? l.description, unitPrice: p?.defaultPrice ?? l.unitPrice })
                }} className="h-8 text-xs">
                  <option value="">— {t('plans.procedure')} —</option>
                  {procedures.data?.map((p) => <option key={p.id} value={p.id}>{p.name} · {money(p.defaultPrice)}</option>)}
                </Select>
                <Input className="h-8 text-xs" value={l.description} onChange={(e) => update(i, { description: e.target.value })} placeholder={t('common.description')} />
              </div>
              <Input className="col-span-1 h-8 text-xs" inputMode="numeric" value={l.toothNumber} onChange={(e) => update(i, { toothNumber: e.target.value.replace(/\D/g, '').slice(0, 2) })} />
              <Input className="col-span-1 h-8 text-xs" type="number" min={1} value={l.quantity} onChange={(e) => update(i, { quantity: Math.max(1, Number(e.target.value)) })} />
              <Input className="col-span-2 h-8 text-xs" type="number" min={0} step="0.001" value={l.unitPrice} onChange={(e) => update(i, { unitPrice: Number(e.target.value) })} />
              <Input className="col-span-2 h-8 text-xs" type="number" min={0} step="0.001" value={l.discount} onChange={(e) => update(i, { discount: Number(e.target.value) })} />
              <Button variant="ghost" size="icon" className="col-span-1 size-8" disabled={lines.length === 1} onClick={() => setLines((ls) => ls.filter((_, j) => j !== i))} aria-label={t('common.delete')}>
                <Trash2 className="size-3.5" />
              </Button>
            </div>
          ))}
          <div className="p-2"><Button variant="ghost" size="sm" icon={<Plus className="size-3.5" />} onClick={() => setLines((ls) => [...ls, newLine()])}>{t('billing.addLine')}</Button></div>
        </div>

        <div className="grid gap-4 sm:grid-cols-3">
          <Field label={t('plans.planDiscount')}><Input type="number" min={0} step="0.001" value={discount} onChange={(e) => setDiscount(Number(e.target.value))} /></Field>
          <Field label={t('billing.dueDate')} hint={t('common.optional')}><Input type="date" min={isoDateInClinic()} value={dueDate} onChange={(e) => setDueDate(e.target.value)} /></Field>
          <Field label={t('common.notes')}><Textarea rows={1} value={notes} onChange={(e) => setNotes(e.target.value)} /></Field>
        </div>
        {error && <p role="alert" className="rounded-lg bg-critical-soft px-3 py-2 text-sm text-critical-ink">{error}</p>}
      </div>
    </Dialog>
  )
}
