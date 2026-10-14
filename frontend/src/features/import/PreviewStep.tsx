import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { AlertTriangle, CheckCircle2, Download, ShieldCheck, XCircle } from 'lucide-react'
import { money } from '@/lib/format'
import type { ImportPreview, ImportRow, RowAction, RowVerdict } from '@/lib/types'
import { Button } from '@/components/ui/Button'
import { ConfirmDialog } from '@/components/ui/Dialog'
import { Badge, Card, EmptyState, Segmented, Select, StatCard, type Tone } from '@/components/ui/primitives'
import { Table, Td, Th, THead, Tr } from '@/components/ui/Table'
import { downloadReport } from './importApi'

type Filter = 'attention' | 'errors' | 'duplicates' | 'all'

const verdictTone: Record<RowVerdict, Tone> = { Ready: 'good', Duplicate: 'warning', PossibleDuplicate: 'warning', AlreadyImported: 'neutral', Error: 'critical' }

/** The dry-run result: what an import would do, row by row, with the choices a person can make. */
export function PreviewStep({ preview, batchId, decisions, onDecisions, onBack, onRecheck, rechecking, onCommit, committing }: {
  preview: ImportPreview; batchId: string; decisions: Record<number, RowAction>; onDecisions: (d: Record<number, RowAction>) => void
  onBack: () => void; onRecheck: () => void; rechecking: boolean; onCommit: () => void; committing: boolean
}) {
  const { t, i18n } = useTranslation()
  const [filter, setFilter] = useState<Filter>('attention')
  const [confirming, setConfirming] = useState(false)
  const s = preview.summary

  // A decision differs from what the server last evaluated: totals are stale until the file is checked again.
  const stale = preview.rows.some((r) => decisions[r.rowNumber] && decisions[r.rowNumber] !== r.action)
  const rows = useMemo(() => preview.rows.filter((r) =>
    filter === 'all' ? true
      : filter === 'errors' ? r.verdict === 'Error'
        : filter === 'duplicates' ? r.verdict === 'Duplicate' || r.verdict === 'PossibleDuplicate' || r.verdict === 'AlreadyImported'
          : r.verdict !== 'Ready' || r.warnings.length > 0), [preview.rows, filter])
  const importable = s.willCreate + s.willUpdate

  return (
    <div className="space-y-5">
      <p role="status" className="flex items-center gap-2 rounded-xl border border-line bg-surface px-4 py-3 text-sm text-ink-2">
        <ShieldCheck className="size-4 shrink-0 text-primary" />{t('import.nothingSaved')}
      </p>

      {preview.mappingProblems.length > 0 && (
        <div role="alert" className="rounded-xl border border-critical/30 bg-critical-soft px-4 py-3 text-sm text-critical-ink">
          <ul className="list-inside list-disc space-y-1">{preview.mappingProblems.map((p) => <li key={p}>{p}</li>)}</ul>
        </div>
      )}

      <div className="grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-6">
        <StatCard label={t('import.summary.total')} value={s.totalRows} />
        <StatCard label={t('import.summary.create')} value={s.willCreate} tone="good" icon={<CheckCircle2 className="size-4" />} />
        <StatCard label={t('import.summary.update')} value={s.willUpdate} />
        <StatCard label={t('import.summary.skip')} value={s.willSkip - s.errors} />
        <StatCard label={t('import.summary.errors')} value={s.errors} tone="critical" icon={s.errors > 0 ? <XCircle className="size-4" /> : undefined} />
        {s.totalAmount != null
          ? <StatCard label={t('import.summary.amount')} value={money(s.totalAmount)} />
          : <StatCard label={t('import.summary.warnings')} value={s.rowsWithWarnings} tone="warning" icon={s.rowsWithWarnings > 0 ? <AlertTriangle className="size-4" /> : undefined} />}
      </div>

      <Card>
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-line px-5 py-3">
          <Segmented<Filter> value={filter} onChange={setFilter} items={[
            { value: 'attention', label: t('import.filter.attention') }, { value: 'errors', label: `${t('import.filter.errors')} (${s.errors})` },
            { value: 'duplicates', label: `${t('import.filter.duplicates')} (${s.duplicates + s.possibleDuplicates + s.alreadyImported})` },
            { value: 'all', label: t('import.filter.all') },
          ]} />
          <div className="flex gap-2">
            <Button size="sm" variant="outline" icon={<Download className="size-3.5" />} onClick={() => void downloadReport(batchId, false)}>{t('import.report')}</Button>
            <Button size="sm" variant="ghost" icon={<Download className="size-3.5" />} onClick={() => void downloadReport(batchId, true)}>{t('import.reportProblems')}</Button>
          </div>
        </div>

        {rows.length === 0 ? (
          <EmptyState icon={<CheckCircle2 className="size-6" />} title={filter === 'attention' && preview.mappingProblems.length === 0 ? t('import.allClean') : t('common.noResults')} />
        ) : (
          <Table>
            <THead>
              <tr><Th className="w-16">{t('import.row')}</Th><Th>{t('import.record')}</Th><Th>{t('import.result')}</Th><Th>{t('import.details')}</Th><Th className="w-52">{t('import.decision')}</Th></tr>
            </THead>
            <tbody>
              {rows.map((r) => <PreviewRow key={r.rowNumber} row={r} decision={decisions[r.rowNumber]} onDecision={(a) => onDecisions({ ...decisions, [r.rowNumber]: a })} />)}
            </tbody>
          </Table>
        )}
        {(preview.rowsTruncated || i18n.language === 'ar') && (
          <p className="border-t border-line px-5 py-3 text-xs text-ink-3">
            {preview.rowsTruncated && t('import.truncated')} {i18n.language === 'ar' && t('import.messagesEnglish')}
          </p>
        )}
      </Card>

      {stale && <p role="status" className="rounded-xl bg-warning-soft px-4 py-3 text-sm text-warning-ink">{t('import.decisionsChanged')}</p>}

      <div className="flex flex-wrap justify-between gap-2">
        <Button variant="outline" onClick={onBack}>{t('common.back')}</Button>
        <div className="flex gap-2">
          <Button variant={stale ? 'primary' : 'outline'} loading={rechecking} onClick={onRecheck}>{t('import.recheck')}</Button>
          <Button disabled={stale || importable === 0 || preview.mappingProblems.length > 0} loading={committing} onClick={() => setConfirming(true)}>
            {importable === 0 ? t('import.nothingToImport') : t('import.importNow', { count: importable })}
          </Button>
        </div>
      </div>

      <ConfirmDialog open={confirming} onClose={() => setConfirming(false)} loading={committing} title={t('import.confirmTitle')}
        body={t('import.confirmBody', { create: s.willCreate, update: s.willUpdate, skip: s.willSkip })} confirmLabel={t('import.importNow', { count: importable })}
        onConfirm={() => { setConfirming(false); onCommit() }} />
    </div>
  )
}

function PreviewRow({ row, decision, onDecision }: { row: ImportRow; decision?: RowAction; onDecision: (a: RowAction) => void }) {
  const { t } = useTranslation()
  const action = decision ?? row.action
  return (
    <Tr>
      <Td className="tabular align-top text-ink-3">{row.rowNumber}</Td>
      <Td className="max-w-56 align-top">
        <span className="block truncate text-sm font-medium text-ink" dir="auto">{row.display}</span>
        {row.matchLabel && <span className="block truncate text-xs text-ink-3" dir="auto">{t('import.matches')}: {row.matchLabel}</span>}
      </Td>
      <Td className="align-top"><Badge tone={verdictTone[row.verdict]}>{t(`import.verdict.${row.verdict}`)}</Badge></Td>
      <Td className="align-top text-sm">
        {row.errors.map((m) => <p key={m} className="text-critical-ink">{m}</p>)}
        {row.warnings.map((m) => <p key={m} className="text-ink-2">{m}</p>)}
      </Td>
      <Td className="align-top">
        {row.allowedActions.length > 1 ? (
          <Select aria-label={t('import.decision')} value={action} onChange={(e) => onDecision(e.target.value as RowAction)}>
            {row.allowedActions.map((a) => <option key={a} value={a}>{t(`import.action.${a}`)}</option>)}
          </Select>
        ) : <span className="text-sm text-ink-3">{t(`import.action.${row.action}`)}</span>}
      </Td>
    </Tr>
  )
}
