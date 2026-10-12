import { useEffect, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { AlertTriangle, Wand2 } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { useDentists } from '@/lib/queries'
import { Permissions } from '@/lib/utils'
import type { ClinicalNote, StructuredNote } from '@/lib/types'
import { useAuth } from '@/auth/AuthProvider'
import { Dialog } from '@/components/ui/Dialog'
import { Button } from '@/components/ui/Button'
import { Badge, Field, Select, Textarea } from '@/components/ui/primitives'

type Fields = {
  chiefComplaint: string; clinicalFindings: string; diagnosis: string; treatmentPerformed: string; recommendations: string
  prescriptionNotes: string; followUpInstructions: string; freeText: string
}
const emptyFields: Fields = {
  chiefComplaint: '', clinicalFindings: '', diagnosis: '', treatmentPerformed: '', recommendations: '', prescriptionNotes: '', followUpInstructions: '', freeText: '',
}

/**
 * Structured clinical note. The dentist can dictate free text and let the AI organise it into sections; the AI only
 * re-arranges the dentist's own words, never adds a diagnosis, and every section stays editable before saving.
 */
export function ClinicalNoteDialog({ open, onClose, patientId, note }: { open: boolean; onClose: () => void; patientId: string; note?: ClinicalNote | null }) {
  const { t } = useTranslation()
  const { user, can } = useAuth()
  const qc = useQueryClient()
  const dentists = useDentists()
  const [fields, setFields] = useState<Fields>(emptyFields)
  const [dentistId, setDentistId] = useState('')
  const [dictation, setDictation] = useState('')
  const [aiUsed, setAiUsed] = useState(false)
  const [aiMeta, setAiMeta] = useState<{ source: string; warnings: string[] } | null>(null)

  useEffect(() => {
    if (!open) return
    setFields(note ? {
      chiefComplaint: note.chiefComplaint ?? '', clinicalFindings: note.clinicalFindings ?? '', diagnosis: note.diagnosis ?? '',
      treatmentPerformed: note.treatmentPerformed ?? '', recommendations: note.recommendations ?? '', prescriptionNotes: note.prescriptionNotes ?? '',
      followUpInstructions: note.followUpInstructions ?? '', freeText: note.freeText ?? '',
    } : emptyFields)
    setDentistId(note?.dentistId ?? user?.dentistId ?? '')
    setDictation('')
    setAiUsed(note?.isAiAssisted ?? false)
    setAiMeta(null)
  }, [open, note, user])

  const structure = useMutation({
    mutationFn: () => api.post<StructuredNote>('/ai/clinical-note/structure', { text: dictation }),
    onSuccess: (r) => {
      setFields((f) => ({
        ...f,
        chiefComplaint: r.chiefComplaint ?? f.chiefComplaint, clinicalFindings: r.clinicalFindings ?? f.clinicalFindings,
        diagnosis: r.assessment ?? f.diagnosis, treatmentPerformed: r.treatmentPerformed ?? f.treatmentPerformed,
        recommendations: r.recommendation ?? f.recommendations, prescriptionNotes: r.prescriptionNotes ?? f.prescriptionNotes,
        followUpInstructions: r.followUp ?? f.followUpInstructions, freeText: f.freeText || dictation,
      }))
      setAiUsed(true)
      setAiMeta({ source: r.source, warnings: r.warnings })
      toast.success(t('notes.structured'))
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  const save = useMutation({
    mutationFn: () => {
      const body = { ...Object.fromEntries(Object.entries(fields).map(([k, v]) => [k, v.trim() || null])), dentistId: dentistId || null, isAiAssisted: aiUsed }
      return note ? api.put(`/patients/${patientId}/clinical-notes/${note.id}`, body) : api.post(`/patients/${patientId}/clinical-notes`, body)
    },
    onSuccess: () => {
      toast.success(t('common.saved'))
      void qc.invalidateQueries({ queryKey: ['clinical-notes', patientId] })
      void qc.invalidateQueries({ queryKey: ['timeline', patientId] })
      onClose()
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  const set = (k: keyof Fields) => (e: React.ChangeEvent<HTMLTextAreaElement>) => setFields((f) => ({ ...f, [k]: e.target.value }))
  const sections: [keyof Fields, string][] = [
    ['chiefComplaint', t('notes.chiefComplaint')], ['clinicalFindings', t('notes.findings')], ['diagnosis', t('notes.diagnosis')],
    ['treatmentPerformed', t('notes.treatmentPerformed')], ['recommendations', t('notes.recommendations')],
    ['prescriptionNotes', t('notes.prescription')], ['followUpInstructions', t('notes.followUp')], ['freeText', t('notes.freeText')],
  ]

  return (
    <Dialog
      open={open}
      onClose={onClose}
      size="xl"
      title={note ? t('notes.title') : t('notes.new')}
      footer={
        <>
          {aiUsed && <Badge tone="recovery" className="me-auto"><Wand2 className="size-3" />{t('notes.aiAssisted')}</Badge>}
          <Button variant="outline" onClick={onClose}>{t('common.cancel')}</Button>
          <Button onClick={() => save.mutate()} loading={save.isPending} disabled={!Object.values(fields).some((v) => v.trim())}>{t('common.save')}</Button>
        </>
      }
    >
      <div className="space-y-5">
        {!user?.dentistId && (
          <Field label={t('common.dentist')} required className="max-w-xs">
            <Select value={dentistId} onChange={(e) => setDentistId(e.target.value)}>
              <option value="">—</option>
              {dentists.data?.map((d) => <option key={d.id} value={d.id}>{d.fullName}</option>)}
            </Select>
          </Field>
        )}

        {can(Permissions.ClinicalManage) && !note && (
          <div className="space-y-2 rounded-xl border border-recovery/30 bg-recovery-soft/60 p-4">
            <Field label={<span className="inline-flex items-center gap-1.5 text-recovery-ink"><Wand2 className="size-3.5" />{t('notes.aiDictation')}</span>} hint={t('notes.aiHint')}>
              <Textarea rows={3} dir="auto" value={dictation} onChange={(e) => setDictation(e.target.value)}
                placeholder="Patient came complaining of pain in upper right molar. Caries visible. X-ray recommended. Patient advised root canal." />
            </Field>
            <Button size="sm" variant="recovery" icon={<Wand2 className="size-3.5" />} loading={structure.isPending} disabled={dictation.trim().length < 10} onClick={() => structure.mutate()}>
              {t('notes.aiAssist')}
            </Button>
            {aiMeta && (
              <div className="space-y-1 text-xs">
                {aiMeta.source === 'built-in' && <p className="text-ink-3">{t('notes.builtIn')}</p>}
                {aiMeta.warnings.filter((w) => !w.startsWith('Structured with built-in')).map((w) => (
                  <p key={w} className="flex gap-1.5 text-warning-ink"><AlertTriangle className="mt-0.5 size-3.5 shrink-0" />{w}</p>
                ))}
              </div>
            )}
          </div>
        )}

        <div className="grid gap-4 sm:grid-cols-2">
          {sections.map(([k, label]) => (
            <Field key={k} label={label} className={k === 'freeText' ? 'sm:col-span-2' : undefined}>
              <Textarea rows={k === 'freeText' ? 3 : 2} dir="auto" value={fields[k]} onChange={set(k)} maxLength={k === 'freeText' ? 8000 : 4000} />
            </Field>
          ))}
        </div>
      </div>
    </Dialog>
  )
}
