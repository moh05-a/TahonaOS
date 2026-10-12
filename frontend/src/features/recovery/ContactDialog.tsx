import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ExternalLink, MessageCircle, Phone } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { useTemplates } from '@/lib/queries'
import { displayName } from '@/lib/utils'
import type { ContactResult, MessageChannel, MessagingStatus, RecoveryPatient } from '@/lib/types'
import { Dialog } from '@/components/ui/Dialog'
import { Button } from '@/components/ui/Button'
import { Field, Segmented, Select, Spinner, Textarea } from '@/components/ui/primitives'
import { IssueBadge } from '@/components/StatusBadge'
import { recoveryAction } from '@/features/recovery/recoveryText'

type Outcome = 'NoResponse' | 'CallBackLater' | 'Declined' | 'AlreadyHandled'

/**
 * Contact a recovery patient by WhatsApp (template rendered with their details) or log a phone call.
 * Every contact updates the patient's follow-ups so later payments are attributed as recovered revenue.
 */
export function ContactDialog({ patient, onClose }: { patient: RecoveryPatient | null; onClose: () => void }) {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const templates = useTemplates(!!patient)
  const status = useQuery({ queryKey: ['messaging-status'], queryFn: () => api.get<MessagingStatus>('/messages/status'), staleTime: 300_000 })
  const [channel, setChannel] = useState<MessageChannel>('WhatsApp')
  const [templateId, setTemplateId] = useState('')
  const [body, setBody] = useState('')
  const [notes, setNotes] = useState('')
  const [done, setDone] = useState<ContactResult | null>(null)

  const suggested = useMemo(() => {
    if (!patient || !templates.data) return undefined
    const ofType = templates.data.filter((x) => x.type === patient.suggestedTemplate && x.isActive)
    return ofType.find((x) => x.language === patient.preferredLanguage) ?? ofType[0]
  }, [patient, templates.data])

  useEffect(() => {
    if (!patient) return
    setChannel('WhatsApp')
    setNotes('')
    setDone(null)
    setBody('')
  }, [patient])
  useEffect(() => {
    if (suggested) setTemplateId(suggested.id)
  }, [suggested])

  // Live preview of the rendered template with the patient's real details.
  const preview = useQuery({
    queryKey: ['render', patient?.patientId, templateId],
    queryFn: () => api.post<{ body: string }>('/messages/render', {
      patientId: patient!.patientId, templateId,
      amount: patient!.primaryIssue === 'OutstandingBalance' ? patient!.issues.find((i) => i.issueType === 'OutstandingBalance')?.value : null,
      treatment: patient!.suggestedTreatment, dentistId: patient!.suggestedDentistId,
    }),
    enabled: !!patient && !!templateId && channel === 'WhatsApp',
  })
  useEffect(() => {
    if (preview.data) setBody(preview.data.body)
  }, [preview.data])

  const contact = useMutation({
    mutationFn: () => api.post<ContactResult>(`/recovery/patients/${patient!.patientId}/contact`, {
      channel, templateId: channel === 'WhatsApp' ? templateId || null : null, body: channel === 'WhatsApp' ? body : null, notes: notes || null,
    }),
    onSuccess: (r) => {
      setDone(r)
      if (r.manualSendUrl) window.open(r.manualSendUrl, '_blank', 'noopener')
      toast.success(t('recovery.contacted'))
      void qc.invalidateQueries({ queryKey: ['recovery'] })
      void qc.invalidateQueries({ queryKey: ['dashboard'] })
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  const outcome = useMutation({
    mutationFn: (o: Outcome) => api.post(`/recovery/patients/${patient!.patientId}/outcome`, { outcome: o, notes: notes || null }),
    onSuccess: () => {
      toast.success(t('recovery.outcomeSaved'))
      void qc.invalidateQueries({ queryKey: ['recovery'] })
      onClose()
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  if (!patient) return null
  const name = displayName(patient.fullName, patient.fullNameAr)

  return (
    <Dialog
      open={!!patient}
      onClose={onClose}
      title={t('recovery.contactTitle', { name })}
      description={<span className="ltr">{patient.whatsAppNumber || patient.phone}</span>}
      size="lg"
      footer={
        done ? (
          <>
            <span className="me-auto text-xs text-ink-3">{t('recovery.outcome')}:</span>
            {(['NoResponse', 'CallBackLater', 'Declined'] as Outcome[]).map((o) => (
              <Button key={o} variant="outline" size="sm" loading={outcome.isPending && outcome.variables === o} onClick={() => outcome.mutate(o)}>
                {t(`recovery.outcomes.${o}`)}
              </Button>
            ))}
            <Button size="sm" onClick={onClose}>{t('common.close')}</Button>
          </>
        ) : (
          <>
            <Button variant="outline" onClick={onClose}>{t('common.cancel')}</Button>
            <Button
              variant={channel === 'WhatsApp' ? 'primary' : 'secondary'}
              icon={channel === 'WhatsApp' ? <MessageCircle className="size-4" /> : <Phone className="size-4" />}
              loading={contact.isPending}
              disabled={channel === 'WhatsApp' && !body.trim()}
              onClick={() => contact.mutate()}
            >
              {channel === 'Phone' ? t('recovery.logCall') : status.data?.isConfigured ? t('recovery.sendWhatsApp') : t('recovery.openWhatsApp')}
            </Button>
          </>
        )
      }
    >
      <div className="space-y-4">
        <div className="flex flex-wrap gap-1.5">
          {patient.issues.map((i) => <IssueBadge key={i.sourceEntityId + i.issueType} issue={i.issueType} />)}
        </div>
        <p className="rounded-lg bg-recovery-soft px-3 py-2 text-sm text-recovery-ink">{recoveryAction(patient, t)}</p>

        {done ? (
          <div className="space-y-3 rounded-xl border border-line p-4 text-sm">
            <p className="font-medium text-ink">{t('recovery.contacted')}</p>
            {done.manualSendUrl && (
              <a href={done.manualSendUrl} target="_blank" rel="noopener noreferrer" className="inline-flex items-center gap-1.5 text-primary-ink hover:underline">
                <ExternalLink className="size-4" /> {t('recovery.openWhatsApp')}
              </a>
            )}
            {done.body && <p className="rounded-lg bg-surface-2 p-3 whitespace-pre-wrap text-ink-2">{done.body}</p>}
          </div>
        ) : (
          <>
            <Field label={t('recovery.channel')}>
              <div>
                <Segmented<MessageChannel>
                  value={channel}
                  onChange={setChannel}
                  items={[{ value: 'WhatsApp', label: t('enum.MessageChannel.WhatsApp') }, { value: 'Phone', label: t('enum.MessageChannel.Phone') }]}
                />
              </div>
            </Field>

            {channel === 'WhatsApp' ? (
              <>
                {!status.data?.isConfigured && <p className="text-xs text-ink-3">{t('recovery.manualSend')}</p>}
                <Field label={t('recovery.template')}>
                  <Select value={templateId} onChange={(e) => setTemplateId(e.target.value)}>
                    {templates.data?.filter((x) => x.isActive && x.channel === 'WhatsApp').map((x) => (
                      <option key={x.id} value={x.id}>{x.name} ({x.language.toUpperCase()})</option>
                    ))}
                  </Select>
                </Field>
                <Field label={<span className="inline-flex items-center gap-2">{t('recovery.message')} {preview.isFetching && <Spinner className="size-3" />}</span>} hint={t('recovery.previewNote')}>
                  <Textarea value={body} onChange={(e) => setBody(e.target.value)} rows={5} dir="auto" maxLength={4000} />
                </Field>
                {preview.error && <p role="alert" className="rounded-lg bg-warning-soft px-3 py-2 text-sm text-warning-ink">{errorMessage(preview.error)}</p>}
              </>
            ) : null}
            <Field label={t('recovery.callNotes')} hint={t('common.optional')}>
              <Textarea value={notes} onChange={(e) => setNotes(e.target.value)} rows={2} maxLength={1000} />
            </Field>
          </>
        )}
      </div>
    </Dialog>
  )
}
