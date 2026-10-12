import { useEffect, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { Lock, Plus, Trash2 } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { money } from '@/lib/format'
import { useDentists, useProcedures } from '@/lib/queries'
import type { TreatmentPlanDetail } from '@/lib/types'
import { useAuth } from '@/auth/AuthProvider'
import { Dialog } from '@/components/ui/Dialog'
import { Button } from '@/components/ui/Button'
import { Field, Input, Select, Textarea } from '@/components/ui/primitives'
import { PatientPicker, type PickedPatient } from '@/components/PatientPicker'

interface Item {
  id?: string; procedureId: string; name: string; toothNumber: string; surfaces: string; quantity: number; unitPrice: number; discount: number; locked?: boolean
}
const newItem = (): Item => ({ procedureId: '', name: '', toothNumber: '', surfaces: '', quantity: 1, unitPrice: 0, discount: 0 })

const validTooth = (v: string) => {
  if (!v) return true
  const n = Number(v)
  const q = Math.floor(n / 10), u = n % 10
  return (q >= 1 && q <= 4 && u >= 1 && u <= 8) || (q >= 5 && q <= 8 && u >= 1 && u <= 5)
}

export function PlanFormDialog({ open, onClose, patient: initialPatient, plan, initialTooth, onSaved }: {
  open: boolean; onClose: () => void; patient?: PickedPatient | null; plan?: TreatmentPlanDetail | null; initialTooth?: number; onSaved?: (id: string) => void
}) {
  const { t } = useTranslation()
  const { user } = useAuth()
  const qc = useQueryClient()
  const procedures = useProcedures()
  const dentists = useDentists()
  const [patient, setPatient] = useState<PickedPatient | null>(null)
  const [dentistId, setDentistId] = useState('')
  const [title, setTitle] = useState('')
  const [notes, setNotes] = useState('')
  const [discount, setDiscount] = useState(0)
  const [items, setItems] = useState<Item[]>([newItem()])
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    setError(null)
    if (plan) {
      const s = plan.summary
      setPatient({ id: s.patientId, fullName: s.patientName, fullNameAr: s.patientNameAr, phone: s.patientPhone })
      setDentistId(s.dentistId)
      setTitle(s.title)
      setNotes(plan.notes ?? '')
      setDiscount(plan.discountAmount)
      setItems(plan.items.filter((i) => i.status !== 'Cancelled').map((i) => ({
        id: i.id, procedureId: i.procedureId ?? '', name: i.name, toothNumber: i.toothNumber?.toString() ?? '', surfaces: i.surfaces ?? '',
        quantity: i.quantity, unitPrice: i.unitPrice, discount: i.discount, locked: i.status === 'Completed',
      })))
    } else {
      setPatient(initialPatient ?? null)
      setDentistId(user?.dentistId ?? '')
      setTitle('')
      setNotes('')
      setDiscount(0)
      setItems([{ ...newItem(), toothNumber: initialTooth?.toString() ?? '' }])
    }
  }, [open, plan, initialPatient, initialTooth, user])

  useEffect(() => {
    if (open && !dentistId && dentists.data?.length) setDentistId(dentists.data[0].id)
  }, [open, dentistId, dentists.data])

  const update = (i: number, patch: Partial<Item>) => setItems((xs) => xs.map((x, j) => (j === i ? { ...x, ...patch } : x)))
  const subtotal = items.reduce((s, i) => s + Math.max(0, i.unitPrice * i.quantity - i.discount), 0)

  const save = useMutation({
    mutationFn: (present: boolean) => {
      const body = {
        patientId: patient!.id, dentistId, title: title.trim(), notes: notes || null, discountAmount: discount, present,
        items: items.map((i) => ({
          id: i.id ?? null, procedureId: i.procedureId || null, name: i.name || null, toothNumber: i.toothNumber ? Number(i.toothNumber) : null,
          surfaces: i.surfaces || null, quantity: i.quantity, unitPrice: i.unitPrice, discount: i.discount,
        })),
      }
      return plan ? api.put(`/treatment-plans/${plan.summary.id}`, body).then(() => plan.summary.id) : api.post<{ id: string }>('/treatment-plans', body).then((r) => r.id)
    },
    onSuccess: (id) => {
      toast.success(plan ? t('common.updated') : t('plans.created'))
      for (const k of ['plans', 'plan', 'patient', 'recovery', 'dashboard', 'chart']) void qc.invalidateQueries({ queryKey: [k] })
      onSaved?.(id)
      onClose()
    },
    onError: (e) => setError(errorMessage(e)),
  })

  const submit = (present: boolean) => {
    if (!patient || !dentistId || !title.trim()) return setError(`${t('common.patient')} / ${t('common.dentist')} / ${t('plans.planTitle')}: ${t('common.required')}`)
    if (items.length === 0 || items.some((i) => !i.procedureId && !i.name.trim())) return setError(`${t('plans.items')}: ${t('common.required')}`)
    if (items.some((i) => !validTooth(i.toothNumber))) return setError(`${t('common.tooth')}: FDI 11–48 / 51–85`)
    setError(null)
    save.mutate(present)
  }

  const isDraft = !plan || plan.summary.status === 'Draft'

  return (
    <Dialog
      open={open}
      onClose={onClose}
      size="xl"
      title={plan ? `${t('plans.edit')} · ${plan.summary.planNumber}` : t('plans.new')}
      footer={
        <>
          <span className="tabular me-auto text-sm text-ink-2">{t('common.total')}: <span className="font-semibold text-ink">{money(Math.max(0, subtotal - discount))}</span></span>
          {isDraft && <Button variant="outline" onClick={() => submit(false)} loading={save.isPending && save.variables === false}>{t('plans.saveDraft')}</Button>}
          <Button onClick={() => submit(isDraft)} loading={save.isPending && save.variables === isDraft}>{isDraft ? t('plans.present') : t('common.save')}</Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="grid gap-4 sm:grid-cols-3">
          <Field label={t('common.patient')} required><PatientPicker value={patient} onChange={setPatient} disabled={!!plan || !!initialPatient} /></Field>
          <Field label={t('common.dentist')} required>
            <Select value={dentistId} onChange={(e) => setDentistId(e.target.value)}>
              {dentists.data?.map((d) => <option key={d.id} value={d.id}>{d.fullName}</option>)}
            </Select>
          </Field>
          <Field label={t('plans.planTitle')} required><Input value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Root canal & crown" maxLength={200} /></Field>
        </div>

        <div className="rounded-xl border border-line">
          <div className="hidden grid-cols-12 gap-2 border-b border-line bg-surface-2 px-3 py-2 text-xs font-medium text-ink-3 sm:grid">
            <span className="col-span-4">{t('plans.procedure')}</span><span className="col-span-1">{t('common.tooth')}</span>
            <span className="col-span-1">{t('plans.surfaces')}</span><span className="col-span-1">{t('plans.qty')}</span>
            <span className="col-span-2">{t('plans.unitPrice')}</span><span className="col-span-2">{t('plans.discount')}</span><span />
          </div>
          {items.map((it, i) => (
            <div key={it.id ?? i} className="grid grid-cols-12 items-center gap-2 border-b border-line px-3 py-2 last:border-0">
              <div className="col-span-12 sm:col-span-4">
                {it.locked ? (
                  <p className="flex items-center gap-1.5 text-sm text-ink-2"><Lock className="size-3.5" />{it.name}</p>
                ) : (
                  <Select value={it.procedureId} className="h-8 text-xs" onChange={(e) => {
                    const p = procedures.data?.find((x) => x.id === e.target.value)
                    update(i, { procedureId: e.target.value, name: p?.name ?? '', unitPrice: p?.defaultPrice ?? 0 })
                  }}>
                    <option value="">— {t('plans.procedure')} —</option>
                    {procedures.data?.map((p) => <option key={p.id} value={p.id}>{p.name} · {money(p.defaultPrice)}</option>)}
                  </Select>
                )}
              </div>
              <Input disabled={it.locked} className="col-span-3 h-8 text-xs sm:col-span-1" inputMode="numeric" placeholder="16" value={it.toothNumber}
                onChange={(e) => update(i, { toothNumber: e.target.value.replace(/\D/g, '').slice(0, 2) })} invalid={!validTooth(it.toothNumber)} />
              <Input disabled={it.locked} className="col-span-3 h-8 text-xs uppercase sm:col-span-1" placeholder="MO" value={it.surfaces}
                onChange={(e) => update(i, { surfaces: e.target.value.toUpperCase().replace(/[^MODBLIF]/g, '').slice(0, 5) })} />
              <Input disabled={it.locked} className="col-span-2 h-8 text-xs sm:col-span-1" type="number" min={1} value={it.quantity} onChange={(e) => update(i, { quantity: Math.max(1, Number(e.target.value)) })} />
              <Input disabled={it.locked} className="col-span-4 h-8 text-xs sm:col-span-2" type="number" min={0} step="0.001" value={it.unitPrice} onChange={(e) => update(i, { unitPrice: Number(e.target.value) })} />
              <Input disabled={it.locked} className="col-span-4 h-8 text-xs sm:col-span-2" type="number" min={0} step="0.001" value={it.discount} onChange={(e) => update(i, { discount: Number(e.target.value) })} />
              <Button variant="ghost" size="icon" className="col-span-2 size-8 sm:col-span-1" disabled={it.locked || items.length === 1}
                onClick={() => setItems((xs) => xs.filter((_, j) => j !== i))} aria-label={t('common.delete')}><Trash2 className="size-3.5" /></Button>
            </div>
          ))}
          <div className="p-2"><Button variant="ghost" size="sm" icon={<Plus className="size-3.5" />} onClick={() => setItems((xs) => [...xs, newItem()])}>{t('plans.addItem')}</Button></div>
        </div>

        <div className="grid gap-4 sm:grid-cols-3">
          <Field label={t('plans.planDiscount')}><Input type="number" min={0} step="0.001" value={discount} onChange={(e) => setDiscount(Number(e.target.value))} /></Field>
          <Field label={t('common.notes')} className="sm:col-span-2"><Textarea rows={1} value={notes} onChange={(e) => setNotes(e.target.value)} maxLength={2000} /></Field>
        </div>
        {error && <p role="alert" className="rounded-lg bg-critical-soft px-3 py-2 text-sm text-critical-ink">{error}</p>}
      </div>
    </Dialog>
  )
}
