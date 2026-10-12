import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient, keepPreviousData } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { CheckCircle2, ExternalLink, MessageCircle, Pencil, Phone, Plus, Sparkles, Trash2, Zap } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { dateTime, relative } from '@/lib/format'
import { displayName, Permissions } from '@/lib/utils'
import { useTemplates } from '@/lib/queries'
import type { Conversation, Message, MessageStatus, MessageTemplate, MessageTemplateType, MessagingStatus, Paged } from '@/lib/types'
import { useAuth } from '@/auth/AuthProvider'
import { Badge, Card, CardHeader, EmptyState, ErrorState, Field, Input, LoadingBlock, PageHeader, Pagination, Select, Switch, Tabs, Textarea } from '@/components/ui/primitives'
import { Button } from '@/components/ui/Button'
import { ConfirmDialog, Dialog } from '@/components/ui/Dialog'
import { MessageStatusBadge } from '@/components/StatusBadge'
import { SendMessageDialog } from './SendMessageDialog'

type Tab = 'log' | 'templates'

export default function MessagesPage() {
  const { t } = useTranslation()
  const { can } = useAuth()
  const [tab, setTab] = useState<Tab>('log')
  const [composing, setComposing] = useState(false)
  const status = useQuery({ queryKey: ['messaging-status'], queryFn: () => api.get<MessagingStatus>('/messages/status'), staleTime: 300_000 })

  return (
    <div className="space-y-5">
      <PageHeader
        title={t('messages.title')}
        subtitle={t('messages.subtitle')}
        actions={can(Permissions.MessagesSend) && <Button icon={<MessageCircle className="size-4" />} onClick={() => setComposing(true)}>{t('messages.compose')}</Button>}
      />
      {status.data && (
        <Card className="flex flex-wrap items-center gap-3 p-4">
          <span className={`grid size-10 place-items-center rounded-xl ${status.data.isConfigured ? 'bg-good-soft text-good-ink' : 'bg-warning-soft text-warning-ink'}`}>
            {status.data.isConfigured ? <Zap className="size-5" /> : <ExternalLink className="size-5" />}
          </span>
          <div className="min-w-0 flex-1">
            <p className="text-sm font-medium text-ink">{t('messages.status')}: {status.data.isConfigured ? t('messages.automatic') : t('messages.manual')}</p>
            <p className="text-xs text-ink-3">{status.data.note}</p>
          </div>
        </Card>
      )}
      <Tabs<Tab> value={tab} onChange={setTab} items={[
        { value: 'log', label: t('messages.log') },
        { value: 'templates', label: t('messages.templates') },
      ]} />
      {tab === 'log' ? <MessageLog /> : <Templates />}
      <SendMessageDialog open={composing} onClose={() => setComposing(false)} />
    </div>
  )
}

function MessageLog() {
  const { t } = useTranslation()
  const { can } = useAuth()
  const qc = useQueryClient()
  const [patientId, setPatientId] = useState<string | null>(null)
  const [statusFilter, setStatusFilter] = useState<MessageStatus | ''>('')
  const [page, setPage] = useState(1)
  const conversations = useQuery({ queryKey: ['conversations'], queryFn: () => api.get<Conversation[]>('/messages/conversations', { take: 60 }) })
  const messages = useQuery({
    queryKey: ['messages', patientId, statusFilter, page],
    queryFn: () => api.get<Paged<Message>>('/messages', { patientId, status: statusFilter, page, pageSize: 30 }),
    placeholderData: keepPreviousData,
  })
  const markSent = useMutation({
    mutationFn: (id: string) => api.post(`/messages/${id}/mark-sent`),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ['messages'] }); void qc.invalidateQueries({ queryKey: ['conversations'] }) },
    onError: (e) => toast.error(errorMessage(e)),
  })

  return (
    <div className="grid gap-5 lg:grid-cols-[320px_1fr]">
      <Card className="max-h-[70vh] overflow-hidden">
        <CardHeader title={t('messages.conversations')} />
        <div className="max-h-[calc(70vh-3.5rem)] overflow-y-auto px-2 pb-2">
          <button onClick={() => { setPatientId(null); setPage(1) }} className={`mb-1 w-full rounded-lg px-3 py-2 text-start text-sm ${!patientId ? 'bg-primary-soft text-primary-ink' : 'text-ink-2 hover:bg-surface-2'}`}>
            {t('common.all')}
          </button>
          {conversations.isLoading ? <LoadingBlock rows={5} /> : conversations.data?.map((c) => (
            <button key={c.patientId} onClick={() => { setPatientId(c.patientId); setPage(1) }}
              className={`w-full rounded-lg px-3 py-2.5 text-start ${patientId === c.patientId ? 'bg-primary-soft' : 'hover:bg-surface-2'}`}>
              <div className="flex items-center justify-between gap-2">
                <span className="truncate text-sm font-medium text-ink">{displayName(c.patientName, c.patientNameAr)}</span>
                <span className="shrink-0 text-[11px] text-ink-3">{relative(c.lastMessageAt)}</span>
              </div>
              <p className="truncate text-xs text-ink-3" dir="auto">{c.lastMessage}</p>
            </button>
          ))}
        </div>
      </Card>

      <Card>
        <div className="flex flex-wrap items-center gap-3 border-b border-line p-4">
          <Select value={statusFilter} onChange={(e) => { setStatusFilter(e.target.value as MessageStatus | ''); setPage(1) }} className="w-56">
            <option value="">{t('common.status')}: {t('common.all')}</option>
            {(['ManualPending', 'Sent', 'Delivered', 'Read', 'Failed', 'Logged'] as MessageStatus[]).map((s) => <option key={s} value={s}>{t(`enum.MessageStatus.${s}`)}</option>)}
          </Select>
          {patientId && <Link to={`/patients/${patientId}`} className="text-sm text-primary-ink hover:underline">{t('recovery.viewPatient')}</Link>}
        </div>
        {messages.error ? <ErrorState error={messages.error} /> : messages.isLoading ? <LoadingBlock rows={6} /> : !messages.data?.items.length ? (
          <EmptyState icon={<MessageCircle className="size-6" />} title={t('messages.noMessages')} />
        ) : (
          <>
            <ul className="divide-y divide-line">
              {messages.data.items.map((m) => (
                <li key={m.id} className="flex gap-3 px-5 py-4">
                  <span className="grid size-9 shrink-0 place-items-center rounded-full bg-surface-3 text-ink-2">
                    {m.channel === 'Phone' ? <Phone className="size-4" /> : <MessageCircle className="size-4" />}
                  </span>
                  <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center gap-2 text-xs text-ink-3">
                      <Link to={`/patients/${m.patientId}`} className="text-sm font-medium text-ink hover:underline">{m.patientName}</Link>
                      <span className="ltr">{m.toNumber}</span>·<span>{dateTime(m.createdAt)}</span>{m.sentBy && <span>· {m.sentBy}</span>}
                      <MessageStatusBadge status={m.status} />
                      {m.isRecovery && <Badge tone="recovery"><Sparkles className="size-3" />{t('nav.recovery')}</Badge>}
                      {m.templateName && <Badge>{m.templateName}</Badge>}
                    </div>
                    <p className="mt-1.5 text-sm whitespace-pre-wrap text-ink-2" dir="auto">{m.body}</p>
                    {m.error && <p className="mt-1 text-xs text-critical-ink">{m.error}</p>}
                  </div>
                  {m.status === 'ManualPending' && can(Permissions.MessagesSend) && (
                    <div className="flex shrink-0 flex-col gap-1.5">
                      {m.toNumber && (
                        <a href={`https://wa.me/${m.toNumber}?text=${encodeURIComponent(m.body)}`} target="_blank" rel="noopener noreferrer">
                          <Button size="sm" variant="outline" icon={<ExternalLink className="size-3.5" />}>WhatsApp</Button>
                        </a>
                      )}
                      <Button size="sm" variant="ghost" icon={<CheckCircle2 className="size-3.5" />} loading={markSent.isPending && markSent.variables === m.id} onClick={() => markSent.mutate(m.id)}>{t('messages.markSent')}</Button>
                    </div>
                  )}
                </li>
              ))}
            </ul>
            <Pagination page={page} totalPages={messages.data.totalPages} onChange={setPage} />
          </>
        )}
      </Card>
    </div>
  )
}

const VARIABLES = ['patient_name', 'doctor', 'date', 'time', 'clinic_name', 'clinic_phone', 'amount', 'treatment']
const TYPES: MessageTemplateType[] = ['AppointmentReminder', 'TreatmentFollowUp', 'MissedAppointment', 'Recall', 'PaymentReminder', 'PostTreatment', 'Custom']

function Templates() {
  const { t } = useTranslation()
  const { can } = useAuth()
  const qc = useQueryClient()
  const templates = useTemplates()
  const [editing, setEditing] = useState<MessageTemplate | null | undefined>(undefined)
  const [deleting, setDeleting] = useState<MessageTemplate | null>(null)
  const canManage = can(Permissions.TemplatesManage)
  const remove = useMutation({
    mutationFn: (id: string) => api.delete(`/messages/templates/${id}`),
    onSuccess: () => { setDeleting(null); void qc.invalidateQueries({ queryKey: ['templates'] }) },
    onError: (e) => toast.error(errorMessage(e)),
  })

  if (templates.error) return <ErrorState error={templates.error} />
  return (
    <div className="space-y-4">
      {canManage && <div className="flex justify-end"><Button icon={<Plus className="size-4" />} onClick={() => setEditing(null)}>{t('messages.newTemplate')}</Button></div>}
      {templates.isLoading ? <LoadingBlock /> : (
        <div className="grid gap-4 md:grid-cols-2">
          {templates.data?.map((tpl) => (
            <Card key={tpl.id} className="flex flex-col p-4">
              <div className="flex items-start justify-between gap-2">
                <div>
                  <p className="font-medium text-ink">{tpl.name}</p>
                  <div className="mt-1 flex flex-wrap gap-1.5">
                    <Badge tone="primary">{t(`enum.MessageTemplateType.${tpl.type}`)}</Badge>
                    <Badge>{tpl.language.toUpperCase()}</Badge>
                    {tpl.isSystem && <Badge>{t('messages.system')}</Badge>}
                    {!tpl.isActive && <Badge tone="warning">{t('staff.inactive')}</Badge>}
                  </div>
                </div>
                {canManage && (
                  <div className="flex gap-1">
                    <Button size="icon" variant="ghost" className="size-8" onClick={() => setEditing(tpl)} aria-label={t('common.edit')}><Pencil className="size-3.5" /></Button>
                    {!tpl.isSystem && <Button size="icon" variant="ghost" className="size-8" onClick={() => setDeleting(tpl)} aria-label={t('common.delete')}><Trash2 className="size-3.5" /></Button>}
                  </div>
                )}
              </div>
              <p className="mt-3 flex-1 rounded-lg bg-surface-2 p-3 text-sm whitespace-pre-wrap text-ink-2" dir={tpl.language === 'ar' ? 'rtl' : 'ltr'}>{tpl.body}</p>
            </Card>
          ))}
        </div>
      )}
      <TemplateDialog template={editing} onClose={() => setEditing(undefined)} />
      <ConfirmDialog open={!!deleting} onClose={() => setDeleting(null)} onConfirm={() => deleting && remove.mutate(deleting.id)} danger loading={remove.isPending}
        title={t('common.delete')} body={t('common.confirmDelete')} />
    </div>
  )
}

function TemplateDialog({ template, onClose }: { template: MessageTemplate | null | undefined; onClose: () => void }) {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const bodyRef = useRef<HTMLTextAreaElement>(null)
  const [form, setForm] = useState({ name: '', type: 'Custom' as MessageTemplateType, language: 'en', body: '', providerTemplateName: '', isActive: true })
  useEffect(() => {
    if (template === undefined) return
    setForm(template ? { name: template.name, type: template.type, language: template.language, body: template.body, providerTemplateName: template.providerTemplateName ?? '', isActive: template.isActive }
      : { name: '', type: 'Custom', language: 'en', body: '', providerTemplateName: '', isActive: true })
  }, [template])

  const save = useMutation({
    mutationFn: () => {
      const body = { ...form, channel: 'WhatsApp', providerTemplateName: form.providerTemplateName || null }
      return template ? api.put(`/messages/templates/${template.id}`, body) : api.post('/messages/templates', body)
    },
    onSuccess: () => { toast.success(t('common.saved')); void qc.invalidateQueries({ queryKey: ['templates'] }); onClose() },
    onError: (e) => toast.error(errorMessage(e)),
  })

  const insert = (v: string) => {
    const el = bodyRef.current
    const token = `{{${v}}}`
    if (!el) return setForm((f) => ({ ...f, body: f.body + token }))
    const start = el.selectionStart, end = el.selectionEnd
    setForm((f) => ({ ...f, body: f.body.slice(0, start) + token + f.body.slice(end) }))
    requestAnimationFrame(() => { el.focus(); el.setSelectionRange(start + token.length, start + token.length) })
  }

  return (
    <Dialog open={template !== undefined} onClose={onClose} size="lg" title={template ? t('common.edit') : t('messages.newTemplate')}
      footer={<><Button variant="outline" onClick={onClose}>{t('common.cancel')}</Button>
        <Button loading={save.isPending} disabled={!form.name.trim() || !form.body.trim()} onClick={() => save.mutate()}>{t('common.save')}</Button></>}>
      <div className="grid gap-4 sm:grid-cols-3">
        <Field label={t('common.name')} required className="sm:col-span-3"><Input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} maxLength={120} /></Field>
        <Field label={t('common.type')}>
          <Select value={form.type} onChange={(e) => setForm({ ...form, type: e.target.value as MessageTemplateType })}>
            {TYPES.map((x) => <option key={x} value={x}>{t(`enum.MessageTemplateType.${x}`)}</option>)}
          </Select>
        </Field>
        <Field label={t('messages.language')}>
          <Select value={form.language} onChange={(e) => setForm({ ...form, language: e.target.value })}><option value="en">English</option><option value="ar">العربية</option></Select>
        </Field>
        <Field label={t('messages.active')}><div className="pt-1.5"><Switch checked={form.isActive} onChange={(v) => setForm({ ...form, isActive: v })} /></div></Field>
        <div className="sm:col-span-3">
          <p className="mb-1.5 text-xs font-medium text-ink-2">{t('messages.variables')}</p>
          <div className="flex flex-wrap gap-1.5">
            {VARIABLES.map((v) => (
              <button key={v} type="button" onClick={() => insert(v)} className="rounded-md bg-primary-soft px-2 py-1 font-mono text-xs text-primary-ink hover:brightness-95">{`{{${v}}}`}</button>
            ))}
          </div>
        </div>
        <Field label={t('messages.body')} required className="sm:col-span-3">
          <Textarea ref={bodyRef} rows={6} value={form.body} onChange={(e) => setForm({ ...form, body: e.target.value })} maxLength={2000} dir={form.language === 'ar' ? 'rtl' : 'ltr'} />
        </Field>
        <Field label={t('messages.providerTemplate')} hint={t('common.optional')} className="sm:col-span-3">
          <Input value={form.providerTemplateName} onChange={(e) => setForm({ ...form, providerTemplateName: e.target.value })} maxLength={120} placeholder="appointment_reminder_v1" />
        </Field>
      </div>
    </Dialog>
  )
}
