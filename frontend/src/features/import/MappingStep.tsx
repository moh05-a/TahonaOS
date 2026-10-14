import { useTranslation } from 'react-i18next'
import { useDentists } from '@/lib/queries'
import type { ImportDefinition, ImportMapping, ImportOptions, ImportUpload } from '@/lib/types'
import { Badge, Card, CardHeader, Field, Input, Select, Switch } from '@/components/ui/primitives'
import { Table, Td, Th, THead, Tr } from '@/components/ui/Table'

/** Match the file's columns to TahonaOS fields and choose how the file should be read. */
export function MappingStep({ definition, upload, mapping, onMapping, options, onOptions }: {
  definition: ImportDefinition; upload: ImportUpload; mapping: ImportMapping; onMapping: (m: ImportMapping) => void
  options: ImportOptions; onOptions: (o: ImportOptions) => void
}) {
  const { t, i18n } = useTranslation()
  const dentists = useDentists()
  const arabic = i18n.language === 'ar'
  const first = upload.sampleRows[0] ?? []
  const label = (key: string) => {
    const field = definition.fields.find((f) => f.key === key)
    return field ? (arabic ? field.labelAr : field.label) : key
  }
  const sampleOf = (header: string | null | undefined) => {
    const index = header ? upload.headers.indexOf(header) : -1
    return index >= 0 ? first[index] : ''
  }
  const set = <K extends keyof ImportOptions>(key: K, value: ImportOptions[K]) => onOptions({ ...options, [key]: value })
  const kind = definition.kind

  return (
    <div className="grid gap-6 xl:grid-cols-[1fr_360px]">
      <Card>
        <CardHeader title={t('import.mapTitle')} subtitle={t('import.mapBody')} />
        {definition.requiredAnyOf.map((group) => !group.some((k) => mapping[k]) && (
          <p key={group.join()} role="alert" className="mx-5 mb-3 rounded-lg bg-warning-soft px-3 py-2 text-sm text-warning-ink">
            {t('import.oneOf', { fields: group.map(label).join(' · ') })}
          </p>
        ))}
        <Table>
          <THead><tr><Th>{t('import.field')}</Th><Th>{t('import.column')}</Th><Th>{t('import.sample')}</Th></tr></THead>
          <tbody>
            {definition.fields.map((field) => {
              const id = `map-${field.key}`
              return (
                <Tr key={field.key}>
                  <Td className="align-top">
                    <label htmlFor={id} className="text-sm font-medium text-ink">{arabic ? field.labelAr : field.label}</label>
                    {field.required && <Badge tone="warning" className="ms-2">{t('import.required')}</Badge>}
                    {field.help && !arabic && <p className="mt-0.5 max-w-xs text-xs text-ink-3">{field.help}</p>}
                  </Td>
                  <Td className="align-top">
                    <Select id={id} className="min-w-44" value={mapping[field.key] ?? ''} invalid={field.required && !mapping[field.key]}
                      onChange={(e) => onMapping({ ...mapping, [field.key]: e.target.value || null })}>
                      <option value="">{t('import.notInFile')}</option>
                      {upload.headers.map((h) => <option key={h} value={h}>{h}</option>)}
                    </Select>
                  </Td>
                  <Td className="max-w-56 truncate align-top text-sm text-ink-3" dir="auto">{sampleOf(mapping[field.key]) || '—'}</Td>
                </Tr>
              )
            })}
          </tbody>
        </Table>
      </Card>

      <Card className="h-fit">
        <CardHeader title={t('import.options')} />
        <div className="space-y-4 px-5 pb-5">
          <Field label={t('import.dateFormat')}>
            <Select value={options.dateFormat} onChange={(e) => set('dateFormat', e.target.value as ImportOptions['dateFormat'])}>
              <option value="dmy">{t('import.dmy')}</option><option value="mdy">{t('import.mdy')}</option><option value="ymd">{t('import.ymd')}</option>
            </Select>
          </Field>

          {kind === 'Patients' && (
            <>
              <Switch checked={options.keepFileNumbers} onChange={(v) => set('keepFileNumbers', v)} label={t('import.keepFileNumbers')} />
              <Switch checked={options.allowMissingPhone} onChange={(v) => set('allowMissingPhone', v)} label={t('import.allowMissingPhone')} />
              <Field label={t('import.duplicateAction')}>
                <Select value={options.duplicateAction} onChange={(e) => set('duplicateAction', e.target.value as ImportOptions['duplicateAction'])}>
                  <option value="skip">{t('import.dupSkip')}</option><option value="fillBlanks">{t('import.dupFill')}</option>
                </Select>
              </Field>
              <Field label={t('import.possibleAction')}>
                <Select value={options.possibleDuplicateAction} onChange={(e) => set('possibleDuplicateAction', e.target.value as ImportOptions['possibleDuplicateAction'])}>
                  <option value="create">{t('import.possCreate')}</option><option value="skip">{t('import.possSkip')}</option>
                </Select>
              </Field>
            </>
          )}

          {(kind === 'Appointments' || kind === 'TreatmentPlans') && (
            <Field label={t('import.defaultDentist')}>
              <Select value={options.defaultDentistId ?? ''} onChange={(e) => set('defaultDentistId', e.target.value || null)}>
                <option value="">{t('import.noDefaultDentist')}</option>
                {(dentists.data ?? []).map((d) => <option key={d.id} value={d.id}>{d.fullName}</option>)}
              </Select>
            </Field>
          )}

          {kind === 'Appointments' && (
            <>
              <Field label={t('import.defaultDuration')}>
                <Input type="number" min={5} max={480} step={5} value={options.defaultDurationMinutes} onChange={(e) => set('defaultDurationMinutes', Number(e.target.value) || 30)} />
              </Field>
              <div>
                <Switch checked={options.assumePastAppointmentsCompleted} onChange={(v) => set('assumePastAppointmentsCompleted', v)} label={t('import.assumeCompleted')} />
                <p className="mt-1.5 text-xs text-ink-3">{t('import.assumeCompletedHint')}</p>
              </div>
            </>
          )}

          {kind === 'TreatmentPlans' && <p className="text-xs leading-relaxed text-ink-3">{t('import.plansNote')}</p>}
        </div>
      </Card>
    </div>
  )
}
