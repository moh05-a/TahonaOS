import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient, keepPreviousData } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { Bot, CheckCircle2, HardDrive, MessageCircle, Pencil, Plus, Trash2, XCircle } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { dateTime, money } from '@/lib/format'
import { Permissions } from '@/lib/utils'
import { useTemplates } from '@/lib/queries'
import { DataSection } from './DataSection'
import { AccountSection, HolidaysEditor, WhatsAppSection } from './AccountSection'
import type {
  AuditLog, ClinicProfile, ClinicSettings, FollowUpRule, FollowUpTrigger, Integrations, Paged, PriorityLevel, Procedure, ProcedureCategory, RecoverySettings,
  ToothConditionType,
} from '@/lib/types'
import { useAuth } from '@/auth/AuthProvider'
import { Badge, Card, CardHeader, ErrorState, Field, Input, LoadingBlock, PageHeader, Pagination, Select, Switch } from '@/components/ui/primitives'
import { Button } from '@/components/ui/Button'
import { ConfirmDialog, Dialog } from '@/components/ui/Dialog'
import { Table, THead, Td, Th, Tr } from '@/components/ui/Table'
import { PriorityBadge } from '@/components/StatusBadge'
import { cn } from '@/lib/utils'

type Section = 'clinic' | 'general' | 'recovery' | 'rules' | 'procedures' | 'integrations' | 'data' | 'account' | 'audit'

export default function SettingsPage() {
  const { t } = useTranslation()
  const { can } = useAuth()
  const [params] = useSearchParams()
  const requested = params.get('tab') as Section | null
  const [section, setSection] = useState<Section>(
    requested && ['clinic', 'general', 'recovery', 'rules', 'procedures', 'integrations', 'data', 'account', 'audit'].includes(requested) ? requested : 'clinic')
  const nav: { value: Section; label: string; hidden?: boolean }[] = [
    { value: 'clinic', label: t('settings.clinic') },
    { value: 'general', label: t('settings.general') },
    { value: 'recovery', label: t('settings.recovery') },
    { value: 'rules', label: t('settings.rules') },
    { value: 'procedures', label: t('settings.procedures') },
    { value: 'integrations', label: t('settings.integrations') },
    { value: 'data', label: t('settings.data'), hidden: !can(Permissions.DataExport) },
    { value: 'account', label: t('settings.account.title') },
    { value: 'audit', label: t('settings.audit'), hidden: !can(Permissions.AuditView) },
  ]
  return (
    <div className="space-y-5">
      <PageHeader title={t('settings.title')} />
      <div className="grid gap-6 lg:grid-cols-[220px_1fr]">
        <nav className="flex gap-1 overflow-x-auto lg:flex-col">
          {nav.filter((n) => !n.hidden).map((n) => (
            <button key={n.value} onClick={() => setSection(n.value)}
              className={cn('rounded-lg px-3 py-2 text-start text-sm font-medium whitespace-nowrap', section === n.value ? 'bg-primary-soft text-primary-ink' : 'text-ink-2 hover:bg-surface-3')}>
              {n.label}
            </button>
          ))}
          <Link to="/messages" className="rounded-lg px-3 py-2 text-sm font-medium whitespace-nowrap text-ink-2 hover:bg-surface-3">{t('settings.templates')} ↗</Link>
        </nav>
        <div className="min-w-0">
          {section === 'clinic' && <ClinicSection />}
          {section === 'general' && <GeneralSection />}
          {section === 'recovery' && <RecoverySection />}
          {section === 'rules' && <RulesSection />}
          {section === 'procedures' && <ProceduresSection />}
          {section === 'data' && <DataSection />}
          {section === 'account' && <AccountSection />}
          {section === 'integrations' && <IntegrationsSection />}
          {section === 'audit' && <AuditSection />}
        </div>
      </div>
    </div>
  )
}

function useSettings() {
  return useQuery({ queryKey: ['settings'], queryFn: () => api.get<ClinicSettings>('/settings') })
}

function ClinicSection() {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const q = useQuery({ queryKey: ['clinic-profile'], queryFn: () => api.get<ClinicProfile>('/settings/clinic') })
  const [f, setF] = useState<ClinicProfile | null>(null)
  useEffect(() => { if (q.data) setF(q.data) }, [q.data])
  const save = useMutation({
    mutationFn: () => api.put('/settings/clinic', f),
    onSuccess: () => { toast.success(t('common.saved')); void qc.invalidateQueries({ queryKey: ['clinic-profile'] }) },
    onError: (e) => toast.error(errorMessage(e)),
  })
  if (q.error) return <ErrorState error={q.error} />
  if (!f) return <LoadingBlock />
  const set = (k: keyof ClinicProfile, v: string) => setF({ ...f, [k]: v })
  return (
    <Card>
      <CardHeader title={t('settings.clinic')} subtitle={f.subscriptionPlan} actions={<Button loading={save.isPending} onClick={() => save.mutate()}>{t('common.save')}</Button>} />
      <div className="grid gap-4 px-5 pb-5 sm:grid-cols-2">
        <Field label={t('common.name')}><Input value={f.name} onChange={(e) => set('name', e.target.value)} /></Field>
        <Field label={t('patients.fields.fullNameAr')}><Input dir="rtl" value={f.nameAr ?? ''} onChange={(e) => set('nameAr', e.target.value)} /></Field>
        <Field label={t('common.phone')}><Input className="ltr" value={f.phone ?? ''} onChange={(e) => set('phone', e.target.value)} /></Field>
        <Field label={t('common.email')}><Input value={f.email ?? ''} onChange={(e) => set('email', e.target.value)} /></Field>
        <Field label={t('patients.fields.address')}><Input value={f.address ?? ''} onChange={(e) => set('address', e.target.value)} /></Field>
        <Field label={t('patients.fields.city')}><Input value={f.city ?? ''} onChange={(e) => set('city', e.target.value)} /></Field>
        <Field label={t('settings.currency')} hint={t('settings.currencyNote')}>
          <Select value={f.currency} onChange={(e) => set('currency', e.target.value)}>
            {['JOD', 'SAR', 'AED', 'USD', 'EUR', 'EGP', 'KWD', 'QAR'].map((c) => <option key={c}>{c}</option>)}
          </Select>
        </Field>
        <Field label={t('settings.timeZone')}><Select value={f.timeZone} onChange={(e) => set('timeZone', e.target.value)}>
          {['Asia/Amman', 'Asia/Riyadh', 'Asia/Dubai', 'Asia/Kuwait', 'Africa/Cairo', 'Europe/London'].map((z) => <option key={z}>{z}</option>)}
        </Select></Field>
        <Field label={t('common.language')}><Select value={f.defaultLanguage} onChange={(e) => set('defaultLanguage', e.target.value)}><option value="ar">العربية</option><option value="en">English</option></Select></Field>
      </div>
    </Card>
  )
}

const dayNames = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']

function GeneralSection() {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const q = useSettings()
  const [f, setF] = useState<ClinicSettings | null>(null)
  useEffect(() => { if (q.data) setF(q.data) }, [q.data])
  const save = useMutation({
    mutationFn: () => api.put('/settings', f),
    onSuccess: () => { toast.success(t('common.saved')); void qc.invalidateQueries({ queryKey: ['settings'] }) },
    onError: (e) => toast.error(errorMessage(e)),
  })
  if (!f) return <LoadingBlock />
  return (
    <Card>
      <CardHeader title={t('settings.general')} actions={<Button loading={save.isPending} onClick={() => save.mutate()}>{t('common.save')}</Button>} />
      <div className="grid gap-4 px-5 pb-5 sm:grid-cols-2 lg:grid-cols-3">
        <Field label={`${t('settings.workingHours')} (${t('common.from')})`}><Input type="time" value={f.workingHoursStart.slice(0, 5)} onChange={(e) => setF({ ...f, workingHoursStart: `${e.target.value}:00` })} /></Field>
        <Field label={`${t('settings.workingHours')} (${t('common.to')})`}><Input type="time" value={f.workingHoursEnd.slice(0, 5)} onChange={(e) => setF({ ...f, workingHoursEnd: `${e.target.value}:00` })} /></Field>
        <Field label={t('settings.slot')}><Select value={f.slotMinutes} onChange={(e) => setF({ ...f, slotMinutes: Number(e.target.value) })}>{[10, 15, 20, 30, 45, 60].map((m) => <option key={m}>{m}</option>)}</Select></Field>
        <div className="sm:col-span-2 lg:col-span-3">
          <p className="mb-1.5 text-xs font-medium text-ink-2">{t('settings.workingDays')}</p>
          <div className="flex flex-wrap gap-1.5">
            {[6, 0, 1, 2, 3, 4, 5].map((d) => {
              const on = f.workingDays.includes(d)
              return <button key={d} type="button" onClick={() => setF({ ...f, workingDays: on ? f.workingDays.filter((x) => x !== d) : [...f.workingDays, d] })}
                className={cn('rounded-lg border px-3 py-1.5 text-sm', on ? 'border-primary bg-primary-soft text-primary-ink' : 'border-line text-ink-3')}>{dayNames[d]}</button>
            })}
          </div>
        </div>
        <div className="sm:col-span-2 lg:col-span-3">
          <HolidaysEditor value={f.holidays} onChange={(holidays) => setF({ ...f, holidays })} />
        </div>
        <Field label={t('settings.reminderHours')}><Input type="number" min={1} max={168} value={f.appointmentReminderHours} onChange={(e) => setF({ ...f, appointmentReminderHours: Number(e.target.value) })} /></Field>
        <Field label={t('settings.invoicePrefix')}><Input value={f.invoicePrefix} onChange={(e) => setF({ ...f, invoicePrefix: e.target.value.toUpperCase() })} maxLength={10} /></Field>
        <Field label={t('settings.dueDays')}><Input type="number" min={0} value={f.invoiceDueDays} onChange={(e) => setF({ ...f, invoiceDueDays: Number(e.target.value) })} /></Field>
        <Field label={t('settings.taxRate')}><Input type="number" min={0} max={50} step="0.1" value={f.taxRatePercent} onChange={(e) => setF({ ...f, taxRatePercent: Number(e.target.value) })} /></Field>
        <div className="flex flex-col items-start gap-3 sm:col-span-2 lg:col-span-3">
          <Switch checked={f.whatsAppEnabled} onChange={(v) => setF({ ...f, whatsAppEnabled: v })} label={t('settings.whatsAppEnabled')} />
          <Switch checked={f.autoSendFollowUps} onChange={(v) => setF({ ...f, autoSendFollowUps: v })} label={t('settings.autoSend')} />
          <Switch checked={f.requireConsentForAutomation} onChange={(v) => setF({ ...f, requireConsentForAutomation: v })} label={t('settings.requireConsent')} />
          <div>
            <p className="mb-1.5 text-xs font-medium text-ink-2">{t('settings.quietHours')}</p>
            <div className="flex flex-wrap items-center gap-2">
              <Input type="time" className="w-32" aria-label={t('common.from')} value={f.quietHoursStart.slice(0, 5)} onChange={(e) => setF({ ...f, quietHoursStart: `${e.target.value}:00` })} />
              <span className="text-sm text-ink-3">{t('common.to')}</span>
              <Input type="time" className="w-32" aria-label={t('common.to')} value={f.quietHoursEnd.slice(0, 5)} onChange={(e) => setF({ ...f, quietHoursEnd: `${e.target.value}:00` })} />
            </div>
            <p className="mt-1.5 text-xs text-ink-3">{t('settings.quietHint')}</p>
          </div>
          <Switch checked={f.aiAssistantEnabled} onChange={(v) => setF({ ...f, aiAssistantEnabled: v })} label={t('settings.aiEnabled')} />
        </div>
      </div>
    </Card>
  )
}

function RecoverySection() {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const q = useSettings()
  const [r, setR] = useState<RecoverySettings | null>(null)
  useEffect(() => { if (q.data) setR(q.data.recovery) }, [q.data])
  const save = useMutation({
    mutationFn: () => api.put('/settings', { ...q.data, recovery: r }),
    onSuccess: () => { toast.success(t('common.saved')); void qc.invalidateQueries({ queryKey: ['settings'] }); void qc.invalidateQueries({ queryKey: ['recovery'] }) },
    onError: (e) => toast.error(errorMessage(e)),
  })
  if (!r) return <LoadingBlock />
  const num = (k: keyof RecoverySettings, label: string, hint?: string, step = 1) => (
    <Field label={label} hint={hint}><Input type="number" step={step} min={0} value={r[k]} onChange={(e) => setR({ ...r, [k]: Number(e.target.value) })} /></Field>
  )
  const totalWeight = r.weightValue + r.weightUrgency + r.weightLikelihood + r.weightSeverity || 1
  return (
    <div className="space-y-6">
      <Card>
        <CardHeader title={t('settings.weights')} subtitle={t('settings.weightsHint')} actions={<Button loading={save.isPending} onClick={() => save.mutate()}>{t('common.save')}</Button>} />
        <div className="grid gap-4 px-5 pb-5 sm:grid-cols-2 lg:grid-cols-4">
          {([['weightValue', 'Value'], ['weightUrgency', 'Urgency'], ['weightLikelihood', 'Likelihood'], ['weightSeverity', 'Severity']] as const).map(([k, name]) => (
            <Field key={k} label={t(`recovery.components.${name}`)} hint={`${Math.round((100 * r[k]) / totalWeight)}%`}>
              <Input type="number" min={0} max={100} value={r[k]} onChange={(e) => setR({ ...r, [k]: Number(e.target.value) })} />
            </Field>
          ))}
        </div>
      </Card>
      <Card>
        <CardHeader title={t('settings.thresholds')} />
        <div className="grid gap-4 px-5 pb-5 sm:grid-cols-2 lg:grid-cols-4">
          {num('highPriorityThreshold', `${t('enum.PriorityLevel.High')} ≥`)}
          {num('mediumPriorityThreshold', `${t('enum.PriorityLevel.Medium')} ≥`)}
          {num('highValueThreshold', t('recovery.highValue'), money(r.highValueThreshold))}
        </div>
      </Card>
      <Card>
        <CardHeader title={t('settings.valuation')} />
        <div className="grid gap-4 px-5 pb-5 sm:grid-cols-3">
          {num('recallEstimatedValue', t('enum.RecoveryIssueType.RecallDue'), undefined, 0.5)}
          {num('missedAppointmentDefaultValue', t('enum.RecoveryIssueType.MissedAppointment'), undefined, 0.5)}
          {num('followUpVisitValue', t('enum.RecoveryIssueType.OverdueFollowUp'), undefined, 0.5)}
        </div>
      </Card>
      <Card>
        <CardHeader title={t('settings.windows')} />
        <div className="grid gap-4 px-5 pb-5 sm:grid-cols-2 lg:grid-cols-4">
          {num('attributionWindowDays', t('settings.attributionWindow'), t('settings.attributionHint'))}
          {num('recontactAfterDays', t('settings.recontact'))}
          {num('missedAppointmentLookbackDays', t('settings.missedLookback'))}
          {num('maxLookbackDays', t('settings.maxLookback'))}
        </div>
      </Card>
    </div>
  )
}

const triggers: FollowUpTrigger[] = ['MissedAppointment', 'TreatmentPlanNotStarted', 'TreatmentPlanInactive', 'TreatmentUnfinished', 'ProcedureCompleted', 'RoutineRecall', 'OverdueBalance']
const categories: ProcedureCategory[] = ['Diagnostic', 'Preventive', 'Restorative', 'Endodontic', 'Prosthodontic', 'OralSurgery', 'Orthodontic', 'Periodontal', 'Cosmetic', 'Implant', 'Pediatric']

function RulesSection() {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const rules = useQuery({ queryKey: ['rules'], queryFn: () => api.get<FollowUpRule[]>('/recovery/rules') })
  const [editing, setEditing] = useState<FollowUpRule | null | undefined>(undefined)
  const [deleting, setDeleting] = useState<FollowUpRule | null>(null)
  const toggle = useMutation({
    mutationFn: (r: FollowUpRule) => api.put(`/recovery/rules/${r.id}`, { ...r, isEnabled: !r.isEnabled }),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ['rules'] }); void qc.invalidateQueries({ queryKey: ['recovery'] }) },
    onError: (e) => toast.error(errorMessage(e)),
  })
  const remove = useMutation({
    mutationFn: (id: string) => api.delete(`/recovery/rules/${id}`),
    onSuccess: () => { setDeleting(null); void qc.invalidateQueries({ queryKey: ['rules'] }) },
    onError: (e) => toast.error(errorMessage(e)),
  })
  return (
    <Card>
      <CardHeader title={t('settings.rules')} subtitle={t('settings.rulesHint')}
        actions={<Button icon={<Plus className="size-4" />} onClick={() => setEditing(null)}>{t('settings.rule.new')}</Button>} />
      {rules.isLoading ? <LoadingBlock /> : (
        <Table>
          <THead><tr><Th>{t('settings.rule.name')}</Th><Th>{t('settings.rule.trigger')}</Th><Th className="text-end">{t('settings.rule.delay')}</Th><Th>{t('common.priority')}</Th><Th>{t('settings.rule.template')}</Th><Th>{t('settings.rule.enabled')}</Th><Th /></tr></THead>
          <tbody>
            {rules.data?.map((r) => (
              <Tr key={r.id}>
                <Td className="font-medium">{r.name}</Td>
                <Td className="text-ink-2">{t(`enum.FollowUpTrigger.${r.trigger}`)}{r.procedureCategory && <Badge className="ms-1.5">{t(`enum.ProcedureCategory.${r.procedureCategory}`)}</Badge>}</Td>
                <Td className="tabular text-end">{r.delayDays}</Td>
                <Td><PriorityBadge priority={r.priority} /></Td>
                <Td className="text-ink-2">{r.messageTemplateName ?? '—'}{r.autoSend && <Badge tone="recovery" className="ms-1.5">{t('settings.rule.autoSend')}</Badge>}</Td>
                <Td><Switch checked={r.isEnabled} onChange={() => toggle.mutate(r)} /></Td>
                <Td className="text-end">
                  <div className="flex justify-end gap-1">
                    <Button size="icon" variant="ghost" className="size-8" onClick={() => setEditing(r)} aria-label={t('common.edit')}><Pencil className="size-3.5" /></Button>
                    <Button size="icon" variant="ghost" className="size-8" onClick={() => setDeleting(r)} aria-label={t('common.delete')}><Trash2 className="size-3.5" /></Button>
                  </div>
                </Td>
              </Tr>
            ))}
          </tbody>
        </Table>
      )}
      <RuleDialog rule={editing} onClose={() => setEditing(undefined)} />
      <ConfirmDialog open={!!deleting} onClose={() => setDeleting(null)} onConfirm={() => deleting && remove.mutate(deleting.id)} danger loading={remove.isPending}
        title={t('common.delete')} body={t('common.confirmDelete')} />
    </Card>
  )
}

function RuleDialog({ rule, onClose }: { rule: FollowUpRule | null | undefined; onClose: () => void }) {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const templates = useTemplates(rule !== undefined)
  const [f, setF] = useState({ name: '', trigger: 'MissedAppointment' as FollowUpTrigger, delayDays: 1, procedureCategory: '' as ProcedureCategory | '', messageTemplateId: '', priority: 'Medium' as PriorityLevel, isEnabled: true, autoSend: false })
  useEffect(() => {
    if (rule === undefined) return
    setF(rule ? { name: rule.name, trigger: rule.trigger, delayDays: rule.delayDays, procedureCategory: rule.procedureCategory ?? '', messageTemplateId: rule.messageTemplateId ?? '', priority: rule.priority, isEnabled: rule.isEnabled, autoSend: rule.autoSend }
      : { name: '', trigger: 'MissedAppointment', delayDays: 1, procedureCategory: '', messageTemplateId: '', priority: 'Medium', isEnabled: true, autoSend: false })
  }, [rule])
  const save = useMutation({
    mutationFn: () => {
      const body = { ...f, procedureCategory: f.trigger === 'ProcedureCompleted' ? f.procedureCategory || null : null, messageTemplateId: f.messageTemplateId || null }
      return rule ? api.put(`/recovery/rules/${rule.id}`, body) : api.post('/recovery/rules', body)
    },
    onSuccess: () => { toast.success(t('common.saved')); void qc.invalidateQueries({ queryKey: ['rules'] }); void qc.invalidateQueries({ queryKey: ['recovery'] }); onClose() },
    onError: (e) => toast.error(errorMessage(e)),
  })
  return (
    <Dialog open={rule !== undefined} onClose={onClose} title={rule ? t('common.edit') : t('settings.rule.new')}
      footer={<><Button variant="outline" onClick={onClose}>{t('common.cancel')}</Button><Button loading={save.isPending} disabled={!f.name.trim()} onClick={() => save.mutate()}>{t('common.save')}</Button></>}>
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label={t('settings.rule.name')} required className="sm:col-span-2"><Input value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} /></Field>
        <Field label={t('settings.rule.trigger')}><Select value={f.trigger} onChange={(e) => setF({ ...f, trigger: e.target.value as FollowUpTrigger })}>{triggers.map((x) => <option key={x} value={x}>{t(`enum.FollowUpTrigger.${x}`)}</option>)}</Select></Field>
        <Field label={t('settings.rule.delay')}><Input type="number" min={0} max={1095} value={f.delayDays} onChange={(e) => setF({ ...f, delayDays: Number(e.target.value) })} /></Field>
        {f.trigger === 'ProcedureCompleted' && (
          <Field label={t('settings.rule.category')}><Select value={f.procedureCategory} onChange={(e) => setF({ ...f, procedureCategory: e.target.value as ProcedureCategory })}>
            <option value="">{t('common.all')}</option>{categories.map((c) => <option key={c} value={c}>{t(`enum.ProcedureCategory.${c}`)}</option>)}
          </Select></Field>
        )}
        <Field label={t('common.priority')}><Select value={f.priority} onChange={(e) => setF({ ...f, priority: e.target.value as PriorityLevel })}>{(['Low', 'Medium', 'High', 'Urgent'] as const).map((p) => <option key={p} value={p}>{t(`enum.PriorityLevel.${p}`)}</option>)}</Select></Field>
        <Field label={t('settings.rule.template')}><Select value={f.messageTemplateId} onChange={(e) => setF({ ...f, messageTemplateId: e.target.value })}>
          <option value="">—</option>{templates.data?.map((x) => <option key={x.id} value={x.id}>{x.name} ({x.language})</option>)}
        </Select></Field>
        <div className="space-y-3 sm:col-span-2">
          <Switch checked={f.isEnabled} onChange={(v) => setF({ ...f, isEnabled: v })} label={t('settings.rule.enabled')} />
          <Switch checked={f.autoSend} onChange={(v) => setF({ ...f, autoSend: v })} label={t('settings.autoSend')} />
        </div>
      </div>
    </Dialog>
  )
}

const conditions: ToothConditionType[] = ['Caries', 'Filling', 'Crown', 'RootCanal', 'Extraction', 'Implant', 'Missing', 'Fracture', 'Bridge', 'Veneer', 'Sealant', 'Other']

function ProceduresSection() {
  const { t } = useTranslation()
  const q = useQuery({ queryKey: ['procedures', 'all'], queryFn: () => api.get<Procedure[]>('/procedures', { includeInactive: true }) })
  const [editing, setEditing] = useState<Procedure | null | undefined>(undefined)
  return (
    <Card>
      <CardHeader title={t('settings.procedures')} actions={<Button icon={<Plus className="size-4" />} onClick={() => setEditing(null)}>{t('common.add')}</Button>} />
      {q.isLoading ? <LoadingBlock /> : (
        <Table>
          <THead><tr><Th>{t('settings.code')}</Th><Th>{t('common.name')}</Th><Th>{t('common.category')}</Th><Th className="text-end">{t('plans.unitPrice')}</Th><Th className="text-end">min</Th><Th>{t('common.status')}</Th><Th /></tr></THead>
          <tbody>
            {q.data?.map((p) => (
              <Tr key={p.id} className={p.isActive ? undefined : 'opacity-50'}>
                <Td className="font-mono text-xs">{p.code}</Td>
                <Td><p className="font-medium">{p.name}</p>{p.nameAr && <p className="text-xs text-ink-3" dir="rtl">{p.nameAr}</p>}</Td>
                <Td className="text-ink-2">{t(`enum.ProcedureCategory.${p.category}`)}</Td>
                <Td className="tabular text-end">{money(p.defaultPrice)}</Td>
                <Td className="tabular text-end">{p.defaultDurationMinutes}</Td>
                <Td><Badge tone={p.isActive ? 'good' : 'neutral'} dot>{p.isActive ? t('staff.active') : t('staff.inactive')}</Badge></Td>
                <Td className="text-end"><Button size="icon" variant="ghost" className="size-8" onClick={() => setEditing(p)} aria-label={t('common.edit')}><Pencil className="size-3.5" /></Button></Td>
              </Tr>
            ))}
          </tbody>
        </Table>
      )}
      <ProcedureDialog procedure={editing} onClose={() => setEditing(undefined)} />
    </Card>
  )
}

function ProcedureDialog({ procedure, onClose }: { procedure: Procedure | null | undefined; onClose: () => void }) {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const [f, setF] = useState({ code: '', name: '', nameAr: '', category: 'Restorative' as ProcedureCategory, defaultPrice: 0, defaultDurationMinutes: 30, resultingCondition: '' as ToothConditionType | '', isActive: true })
  useEffect(() => {
    if (procedure === undefined) return
    setF(procedure ? { code: procedure.code, name: procedure.name, nameAr: procedure.nameAr ?? '', category: procedure.category, defaultPrice: procedure.defaultPrice,
      defaultDurationMinutes: procedure.defaultDurationMinutes, resultingCondition: procedure.resultingCondition ?? '', isActive: procedure.isActive }
      : { code: '', name: '', nameAr: '', category: 'Restorative', defaultPrice: 0, defaultDurationMinutes: 30, resultingCondition: '', isActive: true })
  }, [procedure])
  const save = useMutation({
    mutationFn: () => {
      const body = { ...f, nameAr: f.nameAr || null, resultingCondition: f.resultingCondition || null }
      return procedure ? api.put(`/procedures/${procedure.id}`, body) : api.post('/procedures', body)
    },
    onSuccess: () => { toast.success(t('common.saved')); void qc.invalidateQueries({ queryKey: ['procedures'] }); onClose() },
    onError: (e) => toast.error(errorMessage(e)),
  })
  return (
    <Dialog open={procedure !== undefined} onClose={onClose} title={procedure ? t('common.edit') : t('common.add')}
      footer={<><Button variant="outline" onClick={onClose}>{t('common.cancel')}</Button><Button loading={save.isPending} disabled={!f.code.trim() || !f.name.trim()} onClick={() => save.mutate()}>{t('common.save')}</Button></>}>
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label={t('settings.code')} required><Input value={f.code} onChange={(e) => setF({ ...f, code: e.target.value.toUpperCase() })} maxLength={20} /></Field>
        <Field label={t('common.category')}><Select value={f.category} onChange={(e) => setF({ ...f, category: e.target.value as ProcedureCategory })}>{categories.map((c) => <option key={c} value={c}>{t(`enum.ProcedureCategory.${c}`)}</option>)}</Select></Field>
        <Field label={t('common.name')} required><Input value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} /></Field>
        <Field label={t('patients.fields.fullNameAr')}><Input dir="rtl" value={f.nameAr} onChange={(e) => setF({ ...f, nameAr: e.target.value })} /></Field>
        <Field label={t('plans.unitPrice')}><Input type="number" min={0} step="0.001" value={f.defaultPrice} onChange={(e) => setF({ ...f, defaultPrice: Number(e.target.value) })} /></Field>
        <Field label={t('appointments.duration')}><Input type="number" min={5} max={480} value={f.defaultDurationMinutes} onChange={(e) => setF({ ...f, defaultDurationMinutes: Number(e.target.value) })} /></Field>
        <Field label={t('settings.chartResult')}><Select value={f.resultingCondition} onChange={(e) => setF({ ...f, resultingCondition: e.target.value as ToothConditionType })}>
          <option value="">—</option>{conditions.map((c) => <option key={c} value={c}>{t(`enum.ToothConditionType.${c}`)}</option>)}
        </Select></Field>
        <Field label={t('common.status')}><div className="pt-1.5"><Switch checked={f.isActive} onChange={(v) => setF({ ...f, isActive: v })} /></div></Field>
      </div>
    </Dialog>
  )
}

function IntegrationsSection() {
  const { t } = useTranslation()
  const q = useQuery({ queryKey: ['integrations'], queryFn: () => api.get<Integrations>('/settings/integrations') })
  if (!q.data) return <LoadingBlock />
  const items = [
    { icon: MessageCircle, name: 'WhatsApp Business', ok: q.data.whatsAppConfigured, detail: q.data.whatsAppConfigured ? 'Meta Cloud API' : 'Manual (wa.me links)', env: 'WHATSAPP__APPSECRET, WHATSAPP__WEBHOOKVERIFYTOKEN, SECRETS__ENCRYPTIONKEY' },
    { icon: Bot, name: `AI (${q.data.aiProvider})`, ok: q.data.aiConfigured, detail: q.data.aiModel ?? 'Built-in rule-based mode', env: 'AI__APIKEY' },
    { icon: HardDrive, name: 'File storage', ok: true, detail: q.data.storageProvider, env: 'STORAGE__PROVIDER, STORAGE__S3__BUCKET' },
  ]
  return (
    <div className="grid gap-4 md:grid-cols-2">
      <WhatsAppSection />
      {items.map((i) => (
        <Card key={i.name} className="p-5">
          <div className="flex items-center gap-3">
            <span className="grid size-10 place-items-center rounded-xl bg-surface-3 text-ink-2"><i.icon className="size-5" /></span>
            <div className="flex-1">
              <p className="font-medium text-ink">{i.name}</p>
              <p className="text-xs text-ink-3">{i.detail}</p>
            </div>
            {i.ok ? <Badge tone="good"><CheckCircle2 className="size-3" />{t('settings.configured')}</Badge> : <Badge tone="warning"><XCircle className="size-3" />{t('settings.notConfigured')}</Badge>}
          </div>
          <p className="mt-3 rounded-lg bg-surface-2 px-3 py-2 font-mono text-[11px] text-ink-3">{i.env}</p>
        </Card>
      ))}
    </div>
  )
}

function AuditSection() {
  const { t } = useTranslation()
  const [entity, setEntity] = useState('')
  const [page, setPage] = useState(1)
  const [detail, setDetail] = useState<AuditLog | null>(null)
  const types = useQuery({ queryKey: ['audit-types'], queryFn: () => api.get<string[]>('/audit-logs/entity-types') })
  const q = useQuery({
    queryKey: ['audit', entity, page],
    queryFn: () => api.get<Paged<AuditLog>>('/audit-logs', { entityType: entity, page, pageSize: 30 }),
    placeholderData: keepPreviousData,
  })
  return (
    <Card>
      <CardHeader title={t('settings.audit')} actions={
        <Select value={entity} onChange={(e) => { setEntity(e.target.value); setPage(1) }} className="w-48">
          <option value="">{t('common.all')}</option>{types.data?.map((x) => <option key={x}>{x}</option>)}
        </Select>
      } />
      {q.isLoading ? <LoadingBlock /> : (
        <>
          <Table>
            <THead><tr><Th>{t('common.date')}</Th><Th>{t('settings.auditUser')}</Th><Th>{t('settings.auditAction')}</Th><Th>{t('settings.auditEntity')}</Th><Th>{t('settings.auditChanges')}</Th></tr></THead>
            <tbody>
              {q.data?.items.map((a) => (
                <Tr key={a.id} onClick={() => setDetail(a)}>
                  <Td className="whitespace-nowrap text-ink-2">{dateTime(a.timestamp)}</Td>
                  <Td>{a.userName ?? '—'}</Td>
                  <Td><Badge tone={a.action === 'Created' ? 'good' : a.action === 'Deleted' ? 'critical' : 'info'}>{a.action}</Badge></Td>
                  <Td className="text-ink-2">{a.entityType}</Td>
                  <Td className="max-w-md truncate font-mono text-[11px] text-ink-3">{a.changes ?? '—'}</Td>
                </Tr>
              ))}
            </tbody>
          </Table>
          <Pagination page={page} totalPages={q.data?.totalPages ?? 1} onChange={setPage} />
        </>
      )}
      <Dialog open={!!detail} onClose={() => setDetail(null)} title={`${detail?.action} · ${detail?.entityType}`} description={detail && `${dateTime(detail.timestamp)} · ${detail.userName ?? ''} · ${detail.ipAddress ?? ''}`} size="lg">
        <pre className="overflow-x-auto rounded-lg bg-surface-2 p-3 text-xs text-ink-2" dir="ltr">{detail?.changes ? JSON.stringify(JSON.parse(detail.changes), null, 2) : '—'}</pre>
      </Dialog>
    </Card>
  )
}
