import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { Download, FileSpreadsheet, Plus, RotateCcw, Trash2, Upload } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { dateTime, money } from '@/lib/format'
import type { ImportBatch, ImportUndo } from '@/lib/types'
import { Button } from '@/components/ui/Button'
import { ConfirmDialog } from '@/components/ui/Dialog'
import { Badge, Card, CardHeader, EmptyState, ErrorState, LoadingBlock, PageHeader, type Tone } from '@/components/ui/primitives'
import { Table, Td, Th, THead, Tr } from '@/components/ui/Table'
import { downloadReport } from './importApi'
import { ImportWizard } from './ImportWizard'

const statusTone: Record<ImportBatch['status'], Tone> = { Uploaded: 'warning', Committed: 'good', Undone: 'neutral' }

/** Import centre: past imports, and the wizard for a new one. */
export default function ImportPage() {
  const { t } = useTranslation()
  const qc = useQueryClient()
  // null = history; 'new' = a fresh wizard; an id = resume that upload.
  const [wizard, setWizard] = useState<string | null>(null)
  const [undoing, setUndoing] = useState<ImportBatch | null>(null)

  const batches = useQuery({ queryKey: ['imports'], queryFn: () => api.get<ImportBatch[]>('/import/batches') })
  const refresh = () => {
    void qc.invalidateQueries({ queryKey: ['imports'] })
    void qc.invalidateQueries({ queryKey: ['onboarding'] })
    void qc.invalidateQueries({ queryKey: ['patients'] })
  }

  const undo = useMutation({
    mutationFn: (id: string) => api.post<ImportUndo>(`/import/batches/${id}/undo`),
    onSuccess: (r) => {
      toast.success(t('import.undone', { removed: r.removed, kept: r.kept }), { description: r.notes.join(' ') || undefined })
      setUndoing(null)
      refresh()
    },
    onError: (e) => toast.error(errorMessage(e)),
  })
  const discard = useMutation({
    mutationFn: (id: string) => api.delete(`/import/batches/${id}`),
    onSuccess: refresh,
    onError: (e) => toast.error(errorMessage(e)),
  })

  if (wizard) {
    return <ImportWizard batchId={wizard === 'new' ? null : wizard} onClose={() => { setWizard(null); refresh() }} />
  }

  return (
    <div className="space-y-6">
      <PageHeader icon={<Upload className="size-6 text-primary" />} title={t('import.title')} subtitle={t('import.subtitle')}
        actions={<Button icon={<Plus className="size-4" />} onClick={() => setWizard('new')}>{t('import.new')}</Button>} />

      <Card>
        <CardHeader title={t('import.history')} />
        {batches.error ? <ErrorState error={batches.error} onRetry={() => void batches.refetch()} /> : batches.isLoading ? <LoadingBlock rows={4} /> : !batches.data?.length ? (
          <EmptyState icon={<FileSpreadsheet className="size-6" />} title={t('import.noHistory')} body={t('import.noHistoryBody')}
            action={<Button icon={<Plus className="size-4" />} onClick={() => setWizard('new')}>{t('import.new')}</Button>} />
        ) : (
          <Table>
            <THead>
              <tr><Th>{t('common.date')}</Th><Th>{t('import.steps.file')}</Th><Th>{t('common.status')}</Th><Th>{t('import.result')}</Th><Th className="text-end">{t('common.actions')}</Th></tr>
            </THead>
            <tbody>
              {batches.data.map((b) => (
                <Tr key={b.id}>
                  <Td className="whitespace-nowrap text-ink-2">{dateTime(b.committedAt ?? b.createdAt)}{b.createdBy && <span className="block text-xs text-ink-3">{b.createdBy}</span>}</Td>
                  <Td>
                    <span className="font-medium text-ink">{t(`import.kind.${b.kind}`)}</span>
                    <span className="block max-w-xs truncate text-xs text-ink-3" dir="auto">{b.fileName} · {b.sourceSystem}</span>
                  </Td>
                  <Td>
                    <Badge tone={statusTone[b.status]}>{t(`import.status.${b.status}`)}</Badge>
                    {b.lastError && <span className="mt-1 block max-w-xs text-xs text-critical-ink">{b.lastError}</span>}
                  </Td>
                  <Td className="text-sm text-ink-2">
                    {b.status === 'Uploaded' ? `${b.totalRows} ${t('import.summary.total').toLowerCase()}`
                      : t('import.counts', { created: b.createdCount, updated: b.updatedCount, skipped: b.skippedCount, errors: b.errorCount })}
                    {b.totalAmount != null && b.status !== 'Uploaded' && <span className="block text-xs text-ink-3">{money(b.totalAmount)}</span>}
                  </Td>
                  <Td>
                    <div className="flex justify-end gap-1.5">
                      {b.status === 'Uploaded' && <Button size="sm" onClick={() => setWizard(b.id)}>{t('import.resume')}</Button>}
                      {b.status === 'Uploaded' && (
                        <Button size="sm" variant="ghost" icon={<Trash2 className="size-3.5" />} loading={discard.isPending && discard.variables === b.id}
                          onClick={() => discard.mutate(b.id)}>{t('import.discard')}</Button>
                      )}
                      {b.status !== 'Uploaded' && (
                        <Button size="sm" variant="outline" icon={<Download className="size-3.5" />} onClick={() => void downloadReport(b.id, false)}>{t('import.report')}</Button>
                      )}
                      {b.status === 'Committed' && (
                        <Button size="sm" variant="ghost" icon={<RotateCcw className="size-3.5" />} onClick={() => setUndoing(b)}>{t('import.undo')}</Button>
                      )}
                    </div>
                  </Td>
                </Tr>
              ))}
            </tbody>
          </Table>
        )}
      </Card>

      <ConfirmDialog open={!!undoing} onClose={() => setUndoing(null)} onConfirm={() => undoing && undo.mutate(undoing.id)} danger loading={undo.isPending}
        title={t('import.undoTitle')} body={t('import.undoBody')} confirmLabel={t('import.undo')} />
    </div>
  )
}
