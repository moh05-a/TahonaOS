import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ClipboardPlus, History, Plus, Trash2 } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { date, money } from '@/lib/format'
import { Permissions } from '@/lib/utils'
import type { ConditionStatus, DentalChart, ToothConditionType, ToothHistoryEntry } from '@/lib/types'
import { useAuth } from '@/auth/AuthProvider'
import { Badge, Card, CardHeader, EmptyState, ErrorState, Field, Input, LoadingBlock, Segmented, Select, Skeleton, Textarea } from '@/components/ui/primitives'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { activeConditions, ChartLegendBar, conditionStyle, Odontogram } from './Odontogram'
import { PlanFormDialog } from '@/features/plans/PlanFormDialog'
import type { PickedPatient } from '@/components/PatientPicker'

const conditionTypes: ToothConditionType[] = ['Caries', 'Filling', 'Crown', 'RootCanal', 'Extraction', 'Implant', 'Missing', 'Fracture', 'Bridge', 'Veneer', 'Sealant', 'Other']
const statuses: ConditionStatus[] = ['Diagnosed', 'Planned', 'Treated', 'Resolved']

/** Full interactive chart for one patient: odontogram + tooth panel (conditions, history, notes, plan from tooth). */
export function PatientChart({ patient }: { patient: PickedPatient }) {
  const { t } = useTranslation()
  const { can } = useAuth()
  const [dentition, setDentition] = useState<'adult' | 'primary' | null>(null)
  const [selected, setSelected] = useState<number | undefined>()

  const chart = useQuery({ queryKey: ['chart', patient.id], queryFn: () => api.get<DentalChart>(`/patients/${patient.id}/chart`) })
  useEffect(() => {
    if (chart.data && dentition === null) setDentition(chart.data.suggestPrimaryDentition ? 'primary' : 'adult')
  }, [chart.data, dentition])

  if (chart.error) return <ErrorState error={chart.error} onRetry={() => void chart.refetch()} />

  return (
    <Card>
      <CardHeader
        title={t('chart.title')}
        actions={
          <Segmented
            value={dentition ?? 'adult'}
            onChange={(v) => { setDentition(v); setSelected(undefined) }}
            items={[{ value: 'adult', label: t('chart.adult') }, { value: 'primary', label: t('chart.pediatric') }]}
          />
        }
      />
      <div className="space-y-5 px-5 pb-5">
        {chart.isLoading || !chart.data ? <Skeleton className="h-72" /> : (
          <div className="overflow-x-auto rounded-xl border border-line bg-surface-2 p-4">
            <div className="min-w-[640px]">
              <Odontogram teeth={chart.data.teeth} primary={dentition === 'primary'} selected={selected} onSelect={setSelected} />
            </div>
          </div>
        )}
        <ChartLegendBar />
      </div>
      <ToothPanel
        patient={patient}
        tooth={selected}
        chart={chart.data}
        canEdit={can(Permissions.ChartEdit)}
        canPlan={can(Permissions.TreatmentManage)}
        onClose={() => setSelected(undefined)}
      />
    </Card>
  )
}

function ToothPanel({ patient, tooth, chart, canEdit, canPlan, onClose }: {
  patient: PickedPatient; tooth?: number; chart?: DentalChart; canEdit: boolean; canPlan: boolean; onClose: () => void
}) {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const [type, setType] = useState<ToothConditionType>('Caries')
  const [status, setStatus] = useState<ConditionStatus>('Diagnosed')
  const [surfaces, setSurfaces] = useState('')
  const [notes, setNotes] = useState('')
  const [toothNotes, setToothNotes] = useState('')
  const [planOpen, setPlanOpen] = useState(false)
  const data = chart?.teeth.find((x) => x.toothNumber === tooth)

  useEffect(() => {
    setType('Caries'); setStatus('Diagnosed'); setSurfaces(''); setNotes('')
    setToothNotes(data?.notes ?? '')
  }, [tooth, data?.notes])

  const history = useQuery({
    queryKey: ['tooth-history', patient.id, tooth],
    queryFn: () => api.get<ToothHistoryEntry[]>(`/patients/${patient.id}/chart/teeth/${tooth}`),
    enabled: tooth != null,
  })

  const invalidate = () => {
    void qc.invalidateQueries({ queryKey: ['chart', patient.id] })
    void qc.invalidateQueries({ queryKey: ['tooth-history', patient.id, tooth] })
  }
  const add = useMutation({
    mutationFn: () => api.post(`/patients/${patient.id}/chart/conditions`, { toothNumber: tooth, type, status, surfaces: surfaces || null, notes: notes || null }),
    onSuccess: () => { toast.success(t('common.saved')); setNotes(''); setSurfaces(''); invalidate() },
    onError: (e) => toast.error(errorMessage(e)),
  })
  const remove = useMutation({
    mutationFn: (id: string) => api.delete(`/patients/${patient.id}/chart/conditions/${id}`),
    onSuccess: () => { toast.success(t('common.deleted')); invalidate() },
    onError: (e) => toast.error(errorMessage(e)),
  })
  const updateStatus = useMutation({
    mutationFn: (v: { id: string; type: ToothConditionType; status: ConditionStatus; surfaces?: string; notes?: string }) =>
      api.put(`/patients/${patient.id}/chart/conditions/${v.id}`, { toothNumber: tooth, type: v.type, status: v.status, surfaces: v.surfaces ?? null, notes: v.notes ?? null }),
    onSuccess: invalidate,
    onError: (e) => toast.error(errorMessage(e)),
  })
  const saveNotes = useMutation({
    mutationFn: () => api.put(`/patients/${patient.id}/chart/teeth/${tooth}/notes`, { notes: toothNotes }),
    onSuccess: () => { toast.success(t('common.saved')); invalidate() },
    onError: (e) => toast.error(errorMessage(e)),
  })

  if (tooth == null) return null
  const conds = data?.conditions ?? []

  return (
    <>
      <Dialog
        side
        open={tooth != null && !planOpen}
        onClose={onClose}
        title={`${t('common.tooth')} ${tooth}`}
        description={activeConditions(data).length ? activeConditions(data).map((c) => t(`enum.ToothConditionType.${c.type}`)).join(' · ') : t('chart.noConditions')}
        footer={canPlan && <Button variant="outline" icon={<ClipboardPlus className="size-4" />} onClick={() => setPlanOpen(true)}>{t('plans.new')}</Button>}
      >
        <div className="space-y-6">
          <section>
            <h4 className="mb-2 text-xs font-semibold tracking-wide text-ink-3 uppercase">{t('chart.conditions')}</h4>
            {conds.length === 0 ? <p className="text-sm text-ink-3">{t('chart.noConditions')}</p> : (
              <ul className="space-y-2">
                {conds.map((c) => (
                  <li key={c.id} className="flex items-start gap-3 rounded-xl border border-line p-3">
                    <span className="mt-1 size-3 shrink-0 rounded" style={{ background: conditionStyle[c.type].fill }} />
                    <div className="min-w-0 flex-1">
                      <p className="text-sm font-medium text-ink">
                        {t(`enum.ToothConditionType.${c.type}`)}{c.surfaces && <span className="ms-1.5 text-xs text-ink-3">{c.surfaces}</span>}
                      </p>
                      <p className="text-xs text-ink-3">{date(c.recordedAt)}{c.dentistName && ` · ${c.dentistName}`}</p>
                      {c.notes && <p className="mt-1 text-xs text-ink-2">{c.notes}</p>}
                    </div>
                    {canEdit ? (
                      <div className="flex items-center gap-1">
                        <Select value={c.status} className="h-7 w-28 text-xs" onChange={(e) => updateStatus.mutate({ id: c.id, type: c.type, status: e.target.value as ConditionStatus, surfaces: c.surfaces, notes: c.notes })}>
                          {statuses.map((s) => <option key={s} value={s}>{t(`enum.ConditionStatus.${s}`)}</option>)}
                        </Select>
                        <Button variant="ghost" size="icon" className="size-7" onClick={() => remove.mutate(c.id)} aria-label={t('common.delete')}><Trash2 className="size-3.5" /></Button>
                      </div>
                    ) : <Badge>{t(`enum.ConditionStatus.${c.status}`)}</Badge>}
                  </li>
                ))}
              </ul>
            )}
          </section>

          {canEdit && (
            <section className="space-y-3 rounded-xl bg-surface-2 p-4">
              <h4 className="text-xs font-semibold tracking-wide text-ink-3 uppercase">{t('chart.addCondition')}</h4>
              <div className="grid grid-cols-2 gap-3">
                <Field label={t('common.type')}>
                  <Select value={type} onChange={(e) => setType(e.target.value as ToothConditionType)}>
                    {conditionTypes.map((c) => <option key={c} value={c}>{t(`enum.ToothConditionType.${c}`)}</option>)}
                  </Select>
                </Field>
                <Field label={t('common.status')}>
                  <Select value={status} onChange={(e) => setStatus(e.target.value as ConditionStatus)}>
                    {statuses.slice(0, 3).map((s) => <option key={s} value={s}>{t(`enum.ConditionStatus.${s}`)}</option>)}
                  </Select>
                </Field>
                <Field label={t('chart.surfaces')}>
                  <Input value={surfaces} onChange={(e) => setSurfaces(e.target.value.toUpperCase().replace(/[^MODBLIF]/g, '').slice(0, 5))} placeholder="MOD" />
                </Field>
                <Field label={t('common.notes')}><Input value={notes} onChange={(e) => setNotes(e.target.value)} maxLength={1000} /></Field>
              </div>
              <Button size="sm" icon={<Plus className="size-3.5" />} loading={add.isPending} onClick={() => add.mutate()}>{t('common.add')}</Button>
            </section>
          )}

          <section>
            <h4 className="mb-2 text-xs font-semibold tracking-wide text-ink-3 uppercase">{t('chart.toothNotes')}</h4>
            <Textarea rows={2} value={toothNotes} onChange={(e) => setToothNotes(e.target.value)} disabled={!canEdit} maxLength={1000} />
            {canEdit && toothNotes !== (data?.notes ?? '') && (
              <Button size="sm" variant="outline" className="mt-2" loading={saveNotes.isPending} onClick={() => saveNotes.mutate()}>{t('common.save')}</Button>
            )}
          </section>

          <section>
            <h4 className="mb-2 flex items-center gap-1.5 text-xs font-semibold tracking-wide text-ink-3 uppercase"><History className="size-3.5" />{t('chart.history')}</h4>
            {history.isLoading ? <LoadingBlock rows={2} /> : !history.data?.length ? (
              <EmptyState title={t('chart.noHistory')} className="py-6" />
            ) : (
              <ol className="relative space-y-3 border-s border-line ps-4">
                {history.data.map((h, i) => (
                  <li key={i} className="relative">
                    <span className="absolute -start-[21px] top-1.5 size-2.5 rounded-full border-2 border-surface bg-primary" />
                    <p className="text-sm font-medium text-ink">{h.title}{h.amount != null && <span className="ms-2 text-xs text-ink-3">{money(h.amount)}</span>}</p>
                    <p className="text-xs text-ink-3">{date(h.date)}{h.dentistName && ` · ${h.dentistName}`}{h.status && ` · ${h.status}`}</p>
                    {h.detail && <p className="text-xs text-ink-2">{h.detail}</p>}
                  </li>
                ))}
              </ol>
            )}
          </section>
        </div>
      </Dialog>
      <PlanFormDialog open={planOpen} onClose={() => setPlanOpen(false)} patient={patient} initialTooth={tooth} />
    </>
  )
}
