import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { Sparkles } from 'lucide-react'
import { api, ApiError, errorMessage } from '@/lib/api'
import { clinicLocalToUtc, utcToClinicLocal } from '@/lib/format'
import { useDentists, useProcedures } from '@/lib/queries'
import type { Appointment, ClinicSettings, Paged, TreatmentPlanListItem } from '@/lib/types'
import { Dialog } from '@/components/ui/Dialog'
import { Button } from '@/components/ui/Button'
import { Field, Input, Select, Switch, Textarea } from '@/components/ui/primitives'
import { PatientPicker, type PickedPatient } from '@/components/PatientPicker'

export interface AppointmentDraft {
  id?: string
  patient?: PickedPatient | null
  dentistId?: string
  start?: string // UTC ISO
  durationMinutes?: number
  treatment?: string
  notes?: string
  chair?: string
  treatmentPlanId?: string
  followUpId?: string
  fromRecovery?: boolean
}

/** Create or edit an appointment. Used by the calendar, patient profile and Revenue Recovery ("Book"). */
export function AppointmentDialog({ open, onClose, draft, onSaved }: {
  open: boolean; onClose: () => void; draft: AppointmentDraft; onSaved?: (a: Appointment) => void
}) {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const dentists = useDentists()
  const procedures = useProcedures()
  const [patient, setPatient] = useState<PickedPatient | null>(null)
  const [dentistId, setDentistId] = useState('')
  const [start, setStart] = useState('')
  const [duration, setDuration] = useState(30)
  const [treatment, setTreatment] = useState('')
  const [notes, setNotes] = useState('')
  const [chair, setChair] = useState('')
  const [planId, setPlanId] = useState('')
  const [allowOverlap, setAllowOverlap] = useState(false)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [conflict, setConflict] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    setPatient(draft.patient ?? null)
    setDentistId(draft.dentistId ?? '')
    setStart(draft.start ? utcToClinicLocal(draft.start) : defaultStart())
    setDuration(draft.durationMinutes ?? 30)
    setTreatment(draft.treatment ?? '')
    setNotes(draft.notes ?? '')
    setChair(draft.chair ?? '')
    setPlanId(draft.treatmentPlanId ?? '')
    setAllowOverlap(false)
    setErrors({})
    setConflict(null)
  }, [open, draft])

  useEffect(() => {
    if (!dentistId && dentists.data?.length) setDentistId(dentists.data[0].id)
  }, [dentists.data, dentistId])

  // The chosen day against the clinic's closed dates and working days. Booking stays possible: emergencies happen.
  const settings = useQuery({ queryKey: ['settings'], queryFn: () => api.get<ClinicSettings>('/settings'), staleTime: 300_000, enabled: open })
  const chosenDay = start ? start.slice(0, 10) : ''
  const closedReason = !chosenDay || !settings.data ? null
    : settings.data.holidays?.includes(chosenDay) ? 'holiday'
      : !settings.data.workingDays.includes(new Date(`${chosenDay}T12:00:00`).getDay()) ? 'dayOff' : null

  const plans = useQuery({
    queryKey: ['patient-open-plans', patient?.id],
    queryFn: () => api.get<Paged<TreatmentPlanListItem>>('/treatment-plans', { patientId: patient!.id, pageSize: 20 }),
    enabled: open && !!patient,
    select: (d) => d.items.filter((p) => !['Completed', 'Rejected'].includes(p.status)),
  })

  const save = useMutation({
    mutationFn: () => {
      const body = {
        patientId: patient!.id, dentistId, startTime: clinicLocalToUtc(start), durationMinutes: duration, treatment: treatment.trim(),
        notes: notes || null, chair: chair || null, treatmentPlanId: planId || null, followUpId: draft.followUpId ?? null, allowOverlap,
      }
      return draft.id ? api.put<Appointment>(`/appointments/${draft.id}`, body) : api.post<Appointment>('/appointments', body)
    },
    onSuccess: (a) => {
      toast.success(draft.id ? t('common.updated') : t('appointments.created'))
      void qc.invalidateQueries({ queryKey: ['appointments'] })
      void qc.invalidateQueries({ queryKey: ['dashboard'] })
      void qc.invalidateQueries({ queryKey: ['recovery'] })
      void qc.invalidateQueries({ queryKey: ['patient'] })
      onSaved?.(a)
      onClose()
    },
    onError: (e) => {
      if (e instanceof ApiError && e.status === 409) setConflict(e.message)
      else if (e instanceof ApiError && e.status === 400) setErrors(Object.fromEntries(Object.entries(e.fieldErrors).map(([k, v]) => [k, v[0]])))
      else toast.error(errorMessage(e))
    },
  })

  const submit = () => {
    const errs: Record<string, string> = {}
    if (!patient) errs.patientId = t('common.required')
    if (!dentistId) errs.dentistId = t('common.required')
    if (!start) errs.startTime = t('common.required')
    if (!treatment.trim()) errs.treatment = t('common.required')
    setErrors(errs)
    if (Object.keys(errs).length === 0) save.mutate()
  }

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title={draft.id ? t('appointments.edit') : t('appointments.new')}
      description={draft.fromRecovery ? <span className="inline-flex items-center gap-1 text-recovery-ink"><Sparkles className="size-3.5" />{t('appointments.fromRecovery')}</span> : undefined}
      footer={
        <>
          <Button variant="outline" onClick={onClose}>{t('common.cancel')}</Button>
          <Button onClick={submit} loading={save.isPending}>{t('common.save')}</Button>
        </>
      }
    >
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label={t('common.patient')} error={errors.patientId} className="sm:col-span-2" required>
          <PatientPicker value={patient} onChange={setPatient} invalid={!!errors.patientId} disabled={!!draft.id} />
        </Field>
        <Field label={t('common.dentist')} error={errors.dentistId} required>
          <Select value={dentistId} onChange={(e) => setDentistId(e.target.value)} invalid={!!errors.dentistId}>
            {dentists.data?.map((d) => <option key={d.id} value={d.id}>{d.fullName}</option>)}
          </Select>
        </Field>
        <Field label={t('appointments.start')} error={errors.startTime} required>
          <Input type="datetime-local" step={300} value={start} onChange={(e) => setStart(e.target.value)} invalid={!!errors.startTime} />
        </Field>
        {closedReason && (
          <p role="status" className="rounded-lg bg-warning-soft px-3 py-2 text-sm text-warning-ink sm:col-span-2">{t(`appointments.closed.${closedReason}`)}</p>
        )}
        <Field label={t('common.treatment')} error={errors.treatment} required>
          <Input
            list="procedure-names"
            value={treatment}
            onChange={(e) => {
              setTreatment(e.target.value)
              const p = procedures.data?.find((x) => x.name === e.target.value)
              if (p) setDuration(p.defaultDurationMinutes)
            }}
            invalid={!!errors.treatment}
          />
          <datalist id="procedure-names">
            {procedures.data?.map((p) => <option key={p.id} value={p.name} />)}
          </datalist>
        </Field>
        <Field label={t('appointments.duration')} error={errors.durationMinutes}>
          <Select value={duration} onChange={(e) => setDuration(Number(e.target.value))}>
            {[15, 20, 30, 45, 60, 75, 90, 120, 150, 180].map((m) => <option key={m} value={m}>{m}</option>)}
          </Select>
        </Field>
        <Field label={t('appointments.linkedPlan')} hint={t('common.optional')}>
          <Select value={planId} onChange={(e) => setPlanId(e.target.value)} disabled={!patient}>
            <option value="">{t('common.none')}</option>
            {plans.data?.map((p) => <option key={p.id} value={p.id}>{p.planNumber} · {p.title}</option>)}
          </Select>
        </Field>
        <Field label={t('appointments.chair')} hint={t('common.optional')}>
          <Input value={chair} onChange={(e) => setChair(e.target.value)} maxLength={30} />
        </Field>
        <Field label={t('common.notes')} className="sm:col-span-2">
          <Textarea value={notes} onChange={(e) => setNotes(e.target.value)} maxLength={2000} rows={2} />
        </Field>
        {conflict && (
          <div className="space-y-2 rounded-lg bg-warning-soft p-3 text-sm text-warning-ink sm:col-span-2">
            <p>{conflict}</p>
            <Switch checked={allowOverlap} onChange={setAllowOverlap} label={t('appointments.allowOverlap')} />
          </div>
        )}
      </div>
    </Dialog>
  )
}

/** Next half-hour slot (Amman's offset is a whole number of hours, so rounding in UTC is equivalent). */
function defaultStart() {
  const slot = 30 * 60_000
  return utcToClinicLocal(new Date(Math.ceil(Date.now() / slot) * slot).toISOString())
}
