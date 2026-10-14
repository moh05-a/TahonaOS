import { useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import {
  CalendarDays, CircleDollarSign, ClipboardList, Download, Eye, FileImage, FileText, MessageCircle, Phone, Pin, Plus, Sparkles, Stethoscope, Trash2, Upload, Wand2,
} from 'lucide-react'
import { api, errorMessage, http } from '@/lib/api'
import { bytes, date, dateTime, money } from '@/lib/format'
import { Permissions, saveBlob } from '@/lib/utils'
import { PaymentRowActions } from '@/features/billing/PaymentActions'
import type {
  Appointment, ClinicalNote, DocumentCategory, DocumentDto, InvoiceListItem, Message, Paged, PatientNote, PaymentListItem, TimelineEvent,
  TreatmentPlanListItem, TreatmentRecord,
} from '@/lib/types'
import { useAuth } from '@/auth/AuthProvider'
import { Badge, Card, CardHeader, EmptyState, ErrorState, Field, Input, LoadingBlock, ProgressBar, Select, Switch, Textarea } from '@/components/ui/primitives'
import { Button } from '@/components/ui/Button'
import { ConfirmDialog } from '@/components/ui/Dialog'
import { Table, THead, Td, Th, Tr } from '@/components/ui/Table'
import { AppointmentStatusBadge, InvoiceStatusBadge, MessageStatusBadge, PlanStatusBadge } from '@/components/StatusBadge'
import { ClinicalNoteDialog } from './ClinicalNoteDialog'

const timelineIcon: Record<string, React.ElementType> = {
  registration: Sparkles, consultation: Stethoscope, diagnosis: Stethoscope, treatment_plan: ClipboardList, plan_accepted: ClipboardList,
  plan_completed: ClipboardList, appointment: CalendarDays, treatment: Stethoscope, payment: CircleDollarSign, refund: CircleDollarSign, follow_up: MessageCircle,
}
const timelineTone: Record<string, string> = {
  registration: 'bg-primary-soft text-primary-ink', consultation: 'bg-surface-3 text-ink-2', diagnosis: 'bg-warning-soft text-warning-ink',
  treatment_plan: 'bg-recovery-soft text-recovery-ink', plan_accepted: 'bg-recovery-soft text-recovery-ink', plan_completed: 'bg-good-soft text-good-ink',
  appointment: 'bg-surface-3 text-ink-2', treatment: 'bg-primary-soft text-primary-ink', payment: 'bg-good-soft text-good-ink',
  refund: 'bg-serious-soft text-serious-ink', follow_up: 'bg-recovery-soft text-recovery-ink',
}

export function TimelineCard({ patientId }: { patientId: string }) {
  const { t } = useTranslation()
  const q = useQuery({ queryKey: ['timeline', patientId], queryFn: () => api.get<TimelineEvent[]>(`/patients/${patientId}/timeline`) })
  const [all, setAll] = useState(false)
  const items = all ? q.data : q.data?.slice(0, 12)
  return (
    <Card>
      <CardHeader title={t('patients.timeline')} />
      {q.error ? <ErrorState error={q.error} onRetry={() => void q.refetch()} /> : q.isLoading ? <LoadingBlock rows={5} /> : !q.data?.length ? (
        <EmptyState title={t('patients.noTimeline')} />
      ) : (
        <div className="px-5 pb-5">
          <ol className="relative space-y-4 border-s border-line ps-6">
            {items!.map((e, i) => {
              const Icon = timelineIcon[e.kind] ?? Sparkles
              const missed = e.status === 'NoShow'
              return (
                <li key={i} className="relative">
                  <span className={`absolute -start-[37px] grid size-6 place-items-center rounded-full ring-4 ring-surface ${missed ? 'bg-critical-soft text-critical-ink' : timelineTone[e.kind] ?? 'bg-surface-3 text-ink-2'}`}>
                    <Icon className="size-3" />
                  </span>
                  <div className="flex flex-wrap items-baseline justify-between gap-2">
                    <p className="text-sm font-medium text-ink">{e.title}</p>
                    <span className="text-xs text-ink-3">{date(e.date)}</span>
                  </div>
                  <p className="text-xs text-ink-3">
                    {e.description}{e.amount != null && <span className="ms-1.5 font-medium text-ink-2">{money(e.amount)}</span>}
                  </p>
                </li>
              )
            })}
          </ol>
          {q.data.length > 12 && <Button variant="ghost" size="sm" className="mt-3" onClick={() => setAll((a) => !a)}>{all ? t('common.close') : t('common.viewAll')}</Button>}
        </div>
      )}
    </Card>
  )
}

export function AppointmentsTab({ patientId, onBook }: { patientId: string; onBook: () => void }) {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const { can } = useAuth()
  const q = useQuery({ queryKey: ['appointments', 'patient', patientId], queryFn: () => api.get<Appointment[]>('/appointments', { patientId, take: 200 }) })
  return (
    <Card>
      <CardHeader title={t('nav.appointments')} actions={can(Permissions.AppointmentsManage) && <Button size="sm" icon={<Plus className="size-3.5" />} onClick={onBook}>{t('appointments.new')}</Button>} />
      {q.error ? <ErrorState error={q.error} /> : q.isLoading ? <LoadingBlock /> : !q.data?.length ? <EmptyState title={t('appointments.empty')} /> : (
        <Table>
          <THead><tr><Th>{t('common.date')}</Th><Th>{t('common.treatment')}</Th><Th>{t('common.dentist')}</Th><Th>{t('common.status')}</Th></tr></THead>
          <tbody>
            {q.data.map((a) => (
              <Tr key={a.id} onClick={() => navigate(`/appointments?date=${a.startTime.slice(0, 10)}`)}>
                <Td className="whitespace-nowrap">{dateTime(a.startTime)}</Td>
                <Td>{a.treatment}{a.fromRecovery && <Sparkles className="ms-1.5 inline size-3.5 text-recovery" />}</Td>
                <Td className="text-ink-2">{a.dentistName}</Td>
                <Td><AppointmentStatusBadge status={a.status} /></Td>
              </Tr>
            ))}
          </tbody>
        </Table>
      )}
    </Card>
  )
}

export function PlansTab({ patientId, onNew }: { patientId: string; onNew: () => void }) {
  const { t } = useTranslation()
  const { can } = useAuth()
  const navigate = useNavigate()
  const q = useQuery({ queryKey: ['plans', 'patient', patientId], queryFn: () => api.get<Paged<TreatmentPlanListItem>>('/treatment-plans', { patientId, pageSize: 100 }) })
  return (
    <Card>
      <CardHeader title={t('nav.treatmentPlans')} actions={can(Permissions.TreatmentManage) && <Button size="sm" icon={<Plus className="size-3.5" />} onClick={onNew}>{t('plans.new')}</Button>} />
      {q.error ? <ErrorState error={q.error} /> : q.isLoading ? <LoadingBlock /> : !q.data?.items.length ? <EmptyState title={t('plans.empty')} /> : (
        <div className="grid gap-3 p-5 pt-0 md:grid-cols-2">
          {q.data.items.map((p) => (
            <button key={p.id} onClick={() => navigate(`/treatment-plans/${p.id}`)} className="rounded-xl border border-line p-4 text-start transition-colors hover:border-line-strong">
              <div className="flex items-start justify-between gap-2">
                <div className="min-w-0">
                  <p className="truncate font-medium text-ink">{p.title}</p>
                  <p className="text-xs text-ink-3">{p.planNumber} · {p.dentistName} · {date(p.createdAt)}</p>
                </div>
                <PlanStatusBadge status={p.status} />
              </div>
              <div className="mt-3 flex items-baseline justify-between text-sm">
                <span className="tabular font-semibold text-ink">{money(p.totalAmount)}</span>
                <span className="text-xs text-ink-3">{t('plans.paid')} {money(p.paidAmount)} · {p.completedItems}/{p.itemCount}</span>
              </div>
              <ProgressBar value={p.completionPercent} className="mt-2" tone={p.status === 'Completed' ? 'good' : 'primary'} />
            </button>
          ))}
        </div>
      )}
    </Card>
  )
}

export function HistoryTab({ patientId }: { patientId: string }) {
  const { t } = useTranslation()
  const q = useQuery({ queryKey: ['treatments', patientId], queryFn: () => api.get<TreatmentRecord[]>(`/patients/${patientId}/treatments`) })
  return (
    <Card>
      <CardHeader title={t('patients.tabs.history')} />
      {q.error ? <ErrorState error={q.error} /> : q.isLoading ? <LoadingBlock /> : !q.data?.length ? <EmptyState title={t('common.noResults')} /> : (
        <Table>
          <THead><tr><Th>{t('common.date')}</Th><Th>{t('plans.procedure')}</Th><Th>{t('common.tooth')}</Th><Th>{t('common.dentist')}</Th><Th className="text-end">{t('common.amount')}</Th></tr></THead>
          <tbody>
            {q.data.map((r) => (
              <Tr key={r.id}>
                <Td className="whitespace-nowrap">{date(r.performedAt)}</Td>
                <Td><p className="font-medium text-ink">{r.procedureName}</p>{r.category && <p className="text-xs text-ink-3">{t(`enum.ProcedureCategory.${r.category}`)}</p>}</Td>
                <Td className="tabular">{r.toothNumber ?? '—'}{r.surfaces && <span className="ms-1 text-xs text-ink-3">{r.surfaces}</span>}</Td>
                <Td className="text-ink-2">{r.dentistName}</Td>
                <Td className="tabular text-end">{money(r.price)}</Td>
              </Tr>
            ))}
          </tbody>
        </Table>
      )}
    </Card>
  )
}

export function InvoicesTab({ patientId, onNew, onPay }: { patientId: string; onNew: () => void; onPay: (invoiceId?: string) => void }) {
  const { t } = useTranslation()
  const { can } = useAuth()
  const navigate = useNavigate()
  const q = useQuery({ queryKey: ['invoices', 'patient', patientId], queryFn: () => api.get<Paged<InvoiceListItem>>('/invoices', { patientId, pageSize: 100 }) })
  return (
    <Card>
      <CardHeader title={t('billing.invoices')} actions={can(Permissions.InvoicesManage) && <Button size="sm" icon={<Plus className="size-3.5" />} onClick={onNew}>{t('billing.newInvoice')}</Button>} />
      {q.error ? <ErrorState error={q.error} /> : q.isLoading ? <LoadingBlock /> : !q.data?.items.length ? <EmptyState title={t('billing.empty')} /> : (
        <Table>
          <THead><tr><Th>{t('billing.invoice')}</Th><Th>{t('billing.issueDate')}</Th><Th className="text-end">{t('common.total')}</Th><Th className="text-end">{t('billing.balance')}</Th><Th>{t('common.status')}</Th><Th /></tr></THead>
          <tbody>
            {q.data.items.map((i) => (
              <Tr key={i.id} onClick={() => navigate(`/invoices/${i.id}`)}>
                <Td className="font-medium">{i.invoiceNumber}{i.planNumber && <span className="ms-1.5 text-xs text-ink-3">{i.planNumber}</span>}</Td>
                <Td>{date(i.issueDate)}</Td>
                <Td className="tabular text-end">{money(i.total)}</Td>
                <Td className="tabular text-end">{i.balance > 0 ? <span className="font-medium text-critical-ink">{money(i.balance)}</span> : '—'}</Td>
                <Td><InvoiceStatusBadge status={i.status} overdue={i.isOverdue} /></Td>
                <Td className="text-end" onClick={(e) => e.stopPropagation()}>
                  {i.balance > 0 && ['Issued', 'PartiallyPaid'].includes(i.status) && can(Permissions.PaymentsManage) && (
                    <Button size="sm" variant="outline" onClick={() => onPay(i.id)}>{t('billing.recordPayment')}</Button>
                  )}
                </Td>
              </Tr>
            ))}
          </tbody>
        </Table>
      )}
    </Card>
  )
}

export function PaymentsTab({ patientId, onPay }: { patientId: string; onPay: () => void }) {
  const { t } = useTranslation()
  const { can } = useAuth()
  const q = useQuery({ queryKey: ['payments', 'patient', patientId], queryFn: () => api.get<Paged<PaymentListItem>>('/payments', { patientId, pageSize: 100 }) })
  return (
    <Card>
      <CardHeader title={t('billing.payments')} actions={can(Permissions.PaymentsManage) && <Button size="sm" icon={<Plus className="size-3.5" />} onClick={onPay}>{t('billing.recordPayment')}</Button>} />
      {q.error ? <ErrorState error={q.error} /> : q.isLoading ? <LoadingBlock /> : !q.data?.items.length ? <EmptyState title={t('common.noResults')} /> : (
        <Table>
          <THead><tr><Th>{t('billing.receipt')}</Th><Th>{t('common.date')}</Th><Th>{t('billing.invoice')}</Th><Th>{t('common.method')}</Th><Th className="text-end">{t('common.amount')}</Th><Th /></tr></THead>
          <tbody>
            {q.data.items.map((p) => (
              <Tr key={p.id}>
                <Td className="font-medium">{p.receiptNumber}{p.isRecovered && <Badge tone="recovery" className="ms-2"><Sparkles className="size-3" />{t('billing.recovered')}</Badge>}</Td>
                <Td>{dateTime(p.paidAt)}</Td>
                <Td className="text-ink-2">{p.invoiceNumber ?? '—'}</Td>
                <Td>{t(`enum.PaymentMethod.${p.method}`)}{p.type === 'Refund' && <Badge tone="serious" className="ms-2">{t('billing.refund')}</Badge>}</Td>
                <Td className={`tabular text-end font-medium ${p.type === 'Refund' ? 'text-critical-ink' : 'text-ink'}`}>{p.type === 'Refund' ? '−' : ''}{money(p.amount)}</Td>
                <Td className="text-end"><PaymentRowActions payment={p} /></Td>
              </Tr>
            ))}
          </tbody>
        </Table>
      )}
    </Card>
  )
}

export function NotesTab({ patientId }: { patientId: string }) {
  const { t } = useTranslation()
  const { can } = useAuth()
  const qc = useQueryClient()
  const [editing, setEditing] = useState<ClinicalNote | null | undefined>(undefined)
  const [content, setContent] = useState('')
  const [pinned, setPinned] = useState(false)
  const clinical = useQuery({ queryKey: ['clinical-notes', patientId], queryFn: () => api.get<ClinicalNote[]>(`/patients/${patientId}/clinical-notes`), enabled: can(Permissions.ClinicalView) })
  const staff = useQuery({ queryKey: ['patient-notes', patientId], queryFn: () => api.get<PatientNote[]>(`/patients/${patientId}/notes`) })
  const addNote = useMutation({
    mutationFn: () => api.post(`/patients/${patientId}/notes`, { content, isPinned: pinned }),
    onSuccess: () => { setContent(''); setPinned(false); void qc.invalidateQueries({ queryKey: ['patient-notes', patientId] }) },
    onError: (e) => toast.error(errorMessage(e)),
  })
  const removeNote = useMutation({
    mutationFn: (id: string) => api.delete(`/patients/${patientId}/notes/${id}`),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['patient-notes', patientId] }),
    onError: (e) => toast.error(errorMessage(e)),
  })

  const sections: [keyof ClinicalNote, string][] = [
    ['chiefComplaint', t('notes.chiefComplaint')], ['clinicalFindings', t('notes.findings')], ['diagnosis', t('notes.diagnosis')],
    ['treatmentPerformed', t('notes.treatmentPerformed')], ['recommendations', t('notes.recommendations')], ['prescriptionNotes', t('notes.prescription')],
    ['followUpInstructions', t('notes.followUp')], ['freeText', t('notes.freeText')],
  ]

  return (
    <div className="grid gap-6 xl:grid-cols-[1fr_360px]">
      {can(Permissions.ClinicalView) && (
        <Card>
          <CardHeader title={t('patients.clinicalNotes')} actions={can(Permissions.ClinicalManage) && <Button size="sm" icon={<Plus className="size-3.5" />} onClick={() => setEditing(null)}>{t('notes.new')}</Button>} />
          {clinical.isLoading ? <LoadingBlock /> : !clinical.data?.length ? <EmptyState title={t('common.noResults')} /> : (
            <div className="space-y-3 px-5 pb-5">
              {clinical.data.map((n) => (
                <div key={n.id} className="rounded-xl border border-line p-4">
                  <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
                    <p className="text-sm font-medium text-ink">{date(n.noteDate)} · {n.dentistName}</p>
                    <div className="flex items-center gap-2">
                      {n.isAiAssisted && <Badge tone="recovery"><Wand2 className="size-3" />{t('notes.aiAssisted')}</Badge>}
                      {can(Permissions.ClinicalManage) && <Button size="sm" variant="ghost" onClick={() => setEditing(n)}>{t('common.edit')}</Button>}
                    </div>
                  </div>
                  <dl className="grid gap-2 sm:grid-cols-2">
                    {sections.filter(([k]) => n[k]).map(([k, label]) => (
                      <div key={k} className={k === 'freeText' ? 'sm:col-span-2' : undefined}>
                        <dt className="text-[11px] font-medium tracking-wide text-ink-3 uppercase">{label}</dt>
                        <dd className="text-sm whitespace-pre-wrap text-ink-2" dir="auto">{String(n[k])}</dd>
                      </div>
                    ))}
                  </dl>
                </div>
              ))}
            </div>
          )}
        </Card>
      )}
      <Card>
        <CardHeader title={t('patients.staffNotes')} />
        <div className="space-y-3 px-5 pb-5">
          {can(Permissions.PatientsEdit) && (
            <div className="space-y-2">
              <Textarea rows={2} value={content} onChange={(e) => setContent(e.target.value)} placeholder={t('patients.addNote')} maxLength={4000} dir="auto" />
              <div className="flex items-center justify-between">
                <Switch checked={pinned} onChange={setPinned} label={<span className="text-xs text-ink-2">{t('patients.pin')}</span>} />
                <Button size="sm" disabled={!content.trim()} loading={addNote.isPending} onClick={() => addNote.mutate()}>{t('common.add')}</Button>
              </div>
            </div>
          )}
          {staff.data?.map((n) => (
            <div key={n.id} className={`rounded-xl p-3 text-sm ${n.isPinned ? 'bg-warning-soft' : 'bg-surface-2'}`}>
              <p className="whitespace-pre-wrap text-ink" dir="auto">{n.isPinned && <Pin className="me-1 inline size-3.5 text-warning-ink" />}{n.content}</p>
              <div className="mt-1 flex items-center justify-between text-xs text-ink-3">
                <span>{n.authorName ?? '—'} · {date(n.createdAt)}</span>
                {can(Permissions.PatientsEdit) && <button onClick={() => removeNote.mutate(n.id)} aria-label={t('common.delete')} className="hover:text-critical-ink"><Trash2 className="size-3.5" /></button>}
              </div>
            </div>
          ))}
        </div>
      </Card>
      <ClinicalNoteDialog open={editing !== undefined} note={editing} patientId={patientId} onClose={() => setEditing(undefined)} />
    </div>
  )
}

const docCategories: DocumentCategory[] = ['XRay', 'Scan', 'PatientDocument', 'TreatmentImage', 'Prescription', 'Invoice', 'Other']

export function DocumentsTab({ patientId }: { patientId: string }) {
  const { t } = useTranslation()
  const { can } = useAuth()
  const qc = useQueryClient()
  const fileRef = useRef<HTMLInputElement>(null)
  const [category, setCategory] = useState<DocumentCategory>('XRay')
  const [description, setDescription] = useState('')
  const [tooth, setTooth] = useState('')
  const [deleting, setDeleting] = useState<DocumentDto | null>(null)
  const q = useQuery({ queryKey: ['documents', patientId], queryFn: () => api.get<DocumentDto[]>(`/patients/${patientId}/documents`) })

  const upload = useMutation({
    mutationFn: (file: File) => {
      const form = new FormData()
      form.append('file', file)
      form.append('category', category)
      if (description) form.append('description', description)
      if (tooth) form.append('toothNumber', tooth)
      return http.post(`/patients/${patientId}/documents`, form).then((r) => r.data)
    },
    onSuccess: () => { toast.success(t('common.saved')); setDescription(''); setTooth(''); void qc.invalidateQueries({ queryKey: ['documents', patientId] }) },
    onError: (e) => toast.error(errorMessage(e)),
  })
  const remove = useMutation({
    mutationFn: (id: string) => api.delete(`/patients/${patientId}/documents/${id}`),
    onSuccess: () => { setDeleting(null); void qc.invalidateQueries({ queryKey: ['documents', patientId] }) },
    onError: (e) => toast.error(errorMessage(e)),
  })

  const fetchBlob = async (d: DocumentDto, inline: boolean) => {
    try {
      const r = await http.get(`/patients/${patientId}/documents/${d.id}/download`, { params: { inline }, responseType: 'blob' })
      if (inline) window.open(URL.createObjectURL(r.data as Blob), '_blank', 'noopener')
      else saveBlob(r.data as Blob, d.fileName)
    } catch (e) {
      toast.error(errorMessage(e))
    }
  }

  return (
    <Card>
      <CardHeader title={t('patients.tabs.documents')} />
      {can(Permissions.DocumentsManage) && (
        <div className="mx-5 mb-4 grid gap-3 rounded-xl border border-dashed border-line-strong p-4 sm:grid-cols-[180px_1fr_100px_auto] sm:items-end">
          <Field label={t('common.category')}>
            <Select value={category} onChange={(e) => setCategory(e.target.value as DocumentCategory)}>
              {docCategories.map((c) => <option key={c} value={c}>{t(`enum.DocumentCategory.${c}`)}</option>)}
            </Select>
          </Field>
          <Field label={t('common.description')}><Input value={description} onChange={(e) => setDescription(e.target.value)} maxLength={500} /></Field>
          <Field label={t('common.tooth')}><Input inputMode="numeric" value={tooth} onChange={(e) => setTooth(e.target.value.replace(/\D/g, '').slice(0, 2))} /></Field>
          <input ref={fileRef} type="file" className="hidden" accept=".jpg,.jpeg,.png,.webp,.pdf,.dcm,.stl"
            onChange={(e) => { const f = e.target.files?.[0]; if (f) upload.mutate(f); e.target.value = '' }} />
          <Button icon={<Upload className="size-4" />} loading={upload.isPending} onClick={() => fileRef.current?.click()}>{t('common.add')}</Button>
        </div>
      )}
      {q.isLoading ? <LoadingBlock /> : !q.data?.length ? <EmptyState icon={<FileImage className="size-6" />} title={t('common.noResults')} /> : (
        <div className="grid gap-3 p-5 pt-0 sm:grid-cols-2 xl:grid-cols-3">
          {q.data.map((d) => (
            <div key={d.id} className="flex items-start gap-3 rounded-xl border border-line p-3">
              <span className="grid size-10 shrink-0 place-items-center rounded-lg bg-surface-3 text-ink-2">
                {d.contentType.startsWith('image/') ? <FileImage className="size-5" /> : <FileText className="size-5" />}
              </span>
              <div className="min-w-0 flex-1">
                <p className="truncate text-sm font-medium text-ink" title={d.fileName}>{d.fileName}</p>
                <p className="text-xs text-ink-3">{t(`enum.DocumentCategory.${d.category}`)}{d.toothNumber && ` · ${t('common.tooth')} ${d.toothNumber}`} · {bytes(d.sizeBytes)}</p>
                <p className="text-xs text-ink-3">{date(d.createdAt)}{d.uploadedBy && ` · ${d.uploadedBy}`}</p>
                {d.description && <p className="mt-1 text-xs text-ink-2">{d.description}</p>}
              </div>
              <div className="flex flex-col gap-1">
                <Button size="icon" variant="ghost" className="size-7" onClick={() => void fetchBlob(d, true)} aria-label={t('common.view')}><Eye className="size-3.5" /></Button>
                <Button size="icon" variant="ghost" className="size-7" onClick={() => void fetchBlob(d, false)} aria-label="Download"><Download className="size-3.5" /></Button>
                {can(Permissions.DocumentsManage) && <Button size="icon" variant="ghost" className="size-7" onClick={() => setDeleting(d)} aria-label={t('common.delete')}><Trash2 className="size-3.5" /></Button>}
              </div>
            </div>
          ))}
        </div>
      )}
      <ConfirmDialog open={!!deleting} onClose={() => setDeleting(null)} onConfirm={() => deleting && remove.mutate(deleting.id)} danger
        title={t('common.delete')} body={t('common.confirmDelete')} loading={remove.isPending} confirmLabel={t('common.delete')} />
    </Card>
  )
}

export function CommunicationsTab({ patientId, onSend }: { patientId: string; onSend: () => void }) {
  const { t } = useTranslation()
  const { can } = useAuth()
  const q = useQuery({ queryKey: ['messages', 'patient', patientId], queryFn: () => api.get<Paged<Message>>('/messages', { patientId, pageSize: 100 }) })
  return (
    <Card>
      <CardHeader title={t('patients.tabs.communications')} actions={can(Permissions.MessagesSend) && <Button size="sm" icon={<MessageCircle className="size-3.5" />} onClick={onSend}>{t('patients.sendMessage')}</Button>} />
      {q.isLoading ? <LoadingBlock /> : !q.data?.items.length ? <EmptyState icon={<MessageCircle className="size-6" />} title={t('messages.noMessages')} /> : (
        <div className="space-y-3 px-5 pb-5">
          {q.data.items.map((m) => (
            <div key={m.id} className="flex gap-3">
              <span className="grid size-8 shrink-0 place-items-center rounded-full bg-surface-3 text-ink-2">
                {m.channel === 'Phone' ? <Phone className="size-3.5" /> : <MessageCircle className="size-3.5" />}
              </span>
              <div className="min-w-0 flex-1 rounded-xl rounded-ss-sm bg-surface-2 p-3">
                <div className="mb-1 flex flex-wrap items-center gap-2 text-xs text-ink-3">
                  <span>{t(`enum.MessageChannel.${m.channel}`)}</span>·<span>{dateTime(m.createdAt)}</span>
                  {m.sentBy && <span>· {m.sentBy}</span>}
                  <MessageStatusBadge status={m.status} />
                  {m.isRecovery && <Badge tone="recovery"><Sparkles className="size-3" />{t('nav.recovery')}</Badge>}
                </div>
                <p className="text-sm whitespace-pre-wrap text-ink" dir="auto">{m.body}</p>
              </div>
            </div>
          ))}
        </div>
      )}
    </Card>
  )
}

