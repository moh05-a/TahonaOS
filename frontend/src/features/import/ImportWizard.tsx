import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { useMutation, useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { ArrowLeft, CalendarDays, CheckCircle2, ClipboardList, Download, FileUp, Users, Wallet } from 'lucide-react'
import { api, errorMessage, http } from '@/lib/api'
import { date, money } from '@/lib/format'
import { cn } from '@/lib/utils'
import type { ImportCommit, ImportDefinition, ImportKind, ImportMapping, ImportOptions, ImportPreview, ImportUpload, RowAction } from '@/lib/types'
import { Button } from '@/components/ui/Button'
import { Card, ErrorState, Field, Input, LoadingBlock, PageHeader } from '@/components/ui/primitives'
import { defaultOptions, downloadReport, downloadTemplate } from './importApi'
import { MappingStep } from './MappingStep'
import { PreviewStep } from './PreviewStep'

type Step = 'file' | 'map' | 'check' | 'done'
const steps: Step[] = ['file', 'map', 'check', 'done']
const kinds: { kind: ImportKind; icon: React.ElementType }[] = [
  { kind: 'Patients', icon: Users }, { kind: 'Appointments', icon: CalendarDays }, { kind: 'TreatmentPlans', icon: ClipboardList },
  { kind: 'OpeningBalances', icon: Wallet },
]

/** Upload → match columns → check (dry run) → import. Nothing is written to the clinic's records before the last step. */
export function ImportWizard({ batchId, onClose }: { batchId: string | null; onClose: () => void }) {
  const { t } = useTranslation()
  const [step, setStep] = useState<Step>('file')
  const [kind, setKind] = useState<ImportKind>('Patients')
  const [source, setSource] = useState('')
  const [file, setFile] = useState<File | null>(null)
  const [upload, setUpload] = useState<ImportUpload | null>(null)
  const [mapping, setMapping] = useState<ImportMapping>({})
  const [options, setOptions] = useState<ImportOptions>(defaultOptions)
  const [decisions, setDecisions] = useState<Record<number, RowAction>>({})
  const [preview, setPreview] = useState<ImportPreview | null>(null)
  const [result, setResult] = useState<ImportCommit | null>(null)
  const [error, setError] = useState<string | null>(null)
  const fileInput = useRef<HTMLInputElement>(null)

  const definitions = useQuery({ queryKey: ['import', 'definitions'], queryFn: () => api.get<ImportDefinition[]>('/import/definitions'), staleTime: Infinity })

  const adopt = (u: ImportUpload) => {
    setUpload(u)
    setKind(u.batch.kind)
    setMapping(u.savedMapping ?? u.suggestedMapping)
    setOptions({ ...defaultOptions, ...(u.savedOptions ?? {}) })
    setDecisions({})
    setPreview(null)
    setStep('map')
  }

  // Resume an upload that was left unfinished.
  const resume = useQuery({ queryKey: ['import', 'batch', batchId], queryFn: () => api.get<ImportUpload>(`/import/batches/${batchId}`), enabled: !!batchId, gcTime: 0 })
  useEffect(() => { if (resume.data && !upload) adopt(resume.data) }, [resume.data]) // eslint-disable-line react-hooks/exhaustive-deps

  const send = useMutation({
    mutationFn: () => {
      const form = new FormData()
      form.append('file', file!)
      form.append('kind', kind)
      if (source.trim()) form.append('sourceSystem', source.trim())
      return http.post<ImportUpload>('/import/batches', form).then((r) => r.data)
    },
    onMutate: () => setError(null),
    onSuccess: adopt,
    onError: (e) => setError(errorMessage(e)),
  })

  const request = () => ({ mapping, options, decisions })
  const check = useMutation({
    mutationFn: () => api.post<ImportPreview>(`/import/batches/${upload!.batch.id}/preview`, request()),
    onMutate: () => setError(null),
    onSuccess: (p) => { setPreview(p); setStep('check') },
    onError: (e) => setError(errorMessage(e)),
  })
  const commit = useMutation({
    mutationFn: () => api.post<ImportCommit>(`/import/batches/${upload!.batch.id}/commit`, request()),
    onMutate: () => setError(null),
    onSuccess: (r) => { setResult(r); setStep('done') },
    onError: (e) => setError(errorMessage(e)),
  })

  const definition = definitions.data?.find((d) => d.kind === kind)
  if (definitions.error) return <ErrorState error={definitions.error} onRetry={() => void definitions.refetch()} />
  if (resume.error) return <ErrorState error={resume.error} />
  if (!definitions.data || (batchId && !upload)) return <LoadingBlock rows={6} />

  return (
    <div className="space-y-6">
      <PageHeader title={t('import.new')} subtitle={upload ? `${t(`import.kind.${kind}`)} · ${upload.batch.fileName}` : t('import.subtitle')}
        actions={<Button variant="outline" icon={<ArrowLeft className="size-4 rtl:rotate-180" />} onClick={onClose}>{t('import.history')}</Button>} />

      <ol className="flex flex-wrap gap-2 text-sm" aria-label={t('import.new')}>
        {steps.map((s, i) => {
          const state = s === step ? 'current' : steps.indexOf(step) > i ? 'done' : 'todo'
          return (
            <li key={s} aria-current={state === 'current' ? 'step' : undefined}
              className={cn('flex items-center gap-2 rounded-full border px-3 py-1.5', state === 'current' ? 'border-primary bg-primary-soft font-medium text-primary-ink'
                : state === 'done' ? 'border-line text-ink-2' : 'border-line text-ink-3')}>
              {state === 'done' ? <CheckCircle2 className="size-4 text-good" /> : <span className="tabular grid size-4 place-items-center text-xs">{i + 1}</span>}
              {t(`import.steps.${s}`)}
            </li>
          )
        })}
      </ol>

      {error && <div role="alert" className="rounded-xl border border-critical/30 bg-critical-soft px-4 py-3 text-sm text-critical-ink">{error}</div>}

      {step === 'file' && (
        <Card className="space-y-6 p-5">
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            {kinds.map(({ kind: k, icon: Icon }) => (
              <button key={k} type="button" onClick={() => setKind(k)} aria-pressed={kind === k}
                className={cn('rounded-xl border p-4 text-start transition-colors', kind === k ? 'border-primary bg-primary-soft' : 'border-line hover:border-line-strong')}>
                <Icon className={cn('size-5', kind === k ? 'text-primary' : 'text-ink-3')} />
                <p className="mt-2 text-sm font-semibold text-ink">{t(`import.kind.${k}`)}</p>
                <p className="mt-1 text-xs leading-relaxed text-ink-3">{t(`import.kindBody.${k}`)}</p>
              </button>
            ))}
          </div>

          <div className="grid gap-4 sm:grid-cols-2">
            <Field label={t('import.source')} hint={t('import.sourceHint')}>
              <Input value={source} onChange={(e) => setSource(e.target.value)} maxLength={80} placeholder="Excel" />
            </Field>
            <Field label={t('import.chooseFile')} hint={t('import.fileHint')}>
              <input ref={fileInput} type="file" accept=".csv,.txt,.tsv,.xlsx,.xls" onChange={(e) => setFile(e.target.files?.[0] ?? null)}
                className="block w-full text-sm text-ink-2 file:me-3 file:rounded-lg file:border-0 file:bg-surface-3 file:px-3 file:py-2 file:text-sm file:font-medium file:text-ink hover:file:bg-line" />
            </Field>
          </div>

          <div className="flex flex-wrap items-center justify-between gap-3 border-t border-line pt-5">
            <div className="flex flex-wrap items-center gap-2 text-sm text-ink-3">
              <span>{t('import.template')}:</span>
              <Button size="sm" variant="outline" icon={<Download className="size-3.5" />} onClick={() => void downloadTemplate(kind, 'en')}>{t('import.templateEn')}</Button>
              <Button size="sm" variant="outline" icon={<Download className="size-3.5" />} onClick={() => void downloadTemplate(kind, 'ar')}>{t('import.templateAr')}</Button>
            </div>
            <Button icon={<FileUp className="size-4" />} disabled={!file} loading={send.isPending} onClick={() => send.mutate()}>
              {send.isPending ? t('import.uploading') : t('import.upload')}
            </Button>
          </div>
        </Card>
      )}

      {step === 'map' && upload && definition && (
        <>
          {upload.previousImportAt && (
            <div role="status" className="rounded-xl border border-warning/30 bg-warning-soft px-4 py-3 text-sm text-warning-ink">
              {t('import.sameFile', { date: date(upload.previousImportAt) })}
            </div>
          )}
          <MappingStep definition={definition} upload={upload} mapping={mapping} onMapping={setMapping} options={options} onOptions={setOptions} />
          <div className="flex justify-end">
            <Button loading={check.isPending} onClick={() => check.mutate()}>{check.isPending ? t('import.checking') : t('import.check')}</Button>
          </div>
        </>
      )}

      {step === 'check' && upload && preview && (
        <PreviewStep preview={preview} batchId={upload.batch.id} decisions={decisions} onDecisions={setDecisions}
          onBack={() => setStep('map')} onRecheck={() => check.mutate()} rechecking={check.isPending} onCommit={() => commit.mutate()} committing={commit.isPending} />
      )}

      {step === 'done' && result && (
        <Card className="space-y-4 p-6">
          <div className="flex items-center gap-3">
            <CheckCircle2 className="size-6 text-good" />
            <h2 className="text-lg font-semibold text-ink">{t('import.doneTitle')}</h2>
          </div>
          <p className="text-sm text-ink-2">
            {t('import.doneBody', { created: result.batch.createdCount, updated: result.batch.updatedCount, skipped: result.batch.skippedCount, errors: result.batch.errorCount })}
          </p>
          {result.batch.totalAmount != null && <p className="text-sm font-medium text-ink">{t('import.doneAmount', { amount: money(result.batch.totalAmount) })}</p>}
          {result.batch.errorCount > 0 && <p className="rounded-lg bg-warning-soft px-3 py-2 text-sm text-warning-ink">{t('import.fixAndRetry')}</p>}
          <div className="flex flex-wrap gap-2 pt-2">
            <Button variant="outline" icon={<Download className="size-4" />} onClick={() => void downloadReport(result.batch.id, false)}>{t('import.report')}</Button>
            {result.batch.errorCount > 0 && (
              <Button variant="outline" icon={<Download className="size-4" />} onClick={() => void downloadReport(result.batch.id, true)}>{t('import.reportProblems')}</Button>
            )}
            <Button variant="secondary" onClick={onClose}>{t('import.another')}</Button>
            <Link to="/onboarding"><Button variant="ghost">{t('import.toChecklist')}</Button></Link>
          </div>
        </Card>
      )}
    </div>
  )
}
