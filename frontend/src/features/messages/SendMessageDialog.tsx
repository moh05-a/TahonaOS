import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { MessageCircle } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { useTemplates } from '@/lib/queries'
import type { MessagingStatus, SendResult } from '@/lib/types'
import { Dialog } from '@/components/ui/Dialog'
import { Button } from '@/components/ui/Button'
import { Field, Select, Spinner, Textarea } from '@/components/ui/primitives'
import { PatientPicker, type PickedPatient } from '@/components/PatientPicker'

/** Send a WhatsApp message from a template (variables filled server-side) or free text. */
export function SendMessageDialog({ open, onClose, patient: initial }: { open: boolean; onClose: () => void; patient?: PickedPatient | null }) {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const templates = useTemplates(open)
  const status = useQuery({ queryKey: ['messaging-status'], queryFn: () => api.get<MessagingStatus>('/messages/status'), staleTime: 300_000, enabled: open })
  const [patient, setPatient] = useState<PickedPatient | null>(null)
  const [templateId, setTemplateId] = useState('')
  const [body, setBody] = useState('')

  useEffect(() => {
    if (open) { setPatient(initial ?? null); setTemplateId(''); setBody('') }
  }, [open, initial])

  const preview = useQuery({
    queryKey: ['render', patient?.id, templateId],
    queryFn: () => api.post<{ body: string }>('/messages/render', { patientId: patient!.id, templateId }),
    enabled: open && !!patient && !!templateId,
  })
  useEffect(() => { if (preview.data) setBody(preview.data.body) }, [preview.data])

  const send = useMutation({
    mutationFn: () => api.post<SendResult>('/messages/send', { patientId: patient!.id, channel: 'WhatsApp', templateId: templateId || null, body }),
    onSuccess: (r) => {
      if (r.manualSendUrl) window.open(r.manualSendUrl, '_blank', 'noopener')
      toast.success(t('enum.MessageStatus.' + r.status))
      void qc.invalidateQueries({ queryKey: ['messages'] })
      void qc.invalidateQueries({ queryKey: ['conversations'] })
      onClose()
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title={t('messages.compose')}
      description={status.data && (status.data.isConfigured ? t('messages.automatic') : t('messages.manual'))}
      footer={
        <>
          <Button variant="outline" onClick={onClose}>{t('common.cancel')}</Button>
          <Button icon={<MessageCircle className="size-4" />} loading={send.isPending} disabled={!patient || !body.trim()} onClick={() => send.mutate()}>
            {status.data?.isConfigured ? t('recovery.sendWhatsApp') : t('recovery.openWhatsApp')}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <Field label={t('common.patient')} required><PatientPicker value={patient} onChange={setPatient} disabled={!!initial} /></Field>
        <Field label={t('recovery.template')} hint={t('common.optional')}>
          <Select value={templateId} onChange={(e) => setTemplateId(e.target.value)}>
            <option value="">—</option>
            {templates.data?.filter((x) => x.isActive).map((x) => <option key={x.id} value={x.id}>{x.name} ({x.language.toUpperCase()})</option>)}
          </Select>
        </Field>
        <Field label={<span className="inline-flex items-center gap-2">{t('recovery.message')}{preview.isFetching && <Spinner className="size-3" />}</span>}>
          <Textarea rows={5} dir="auto" value={body} onChange={(e) => setBody(e.target.value)} maxLength={4000} />
        </Field>
        {preview.error && <p role="alert" className="rounded-lg bg-warning-soft px-3 py-2 text-sm text-warning-ink">{errorMessage(preview.error)}</p>}
      </div>
    </Dialog>
  )
}
