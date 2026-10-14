import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { Download, FileArchive } from 'lucide-react'
import { errorMessage, http } from '@/lib/api'
import { saveBlob } from '@/lib/utils'
import { Button } from '@/components/ui/Button'
import { Card, CardHeader, Switch } from '@/components/ui/primitives'

/** Take the clinic's records out: one ZIP of CSV files with a manifest. Available even when the account is read-only. */
export function DataSection() {
  const { t } = useTranslation()
  const [includeDocuments, setIncludeDocuments] = useState(false)
  const [busy, setBusy] = useState(false)

  const run = async () => {
    setBusy(true)
    try {
      // Large clinics and uploaded files take a while to package.
      const r = await http.post('/export/full', null, { params: { includeDocuments }, responseType: 'blob', timeout: 10 * 60_000 })
      const header = r.headers['content-disposition'] as string | undefined
      const name = header?.match(/filename\*=UTF-8''([^;]+)/i)?.[1] ?? header?.match(/filename="?([^";]+)"?/i)?.[1] ?? 'tahona-export.zip'
      saveBlob(r.data as Blob, decodeURIComponent(name))
      toast.success(t('settings.exportDone'))
    } catch (e) {
      toast.error(errorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card>
      <CardHeader title={t('settings.data')} subtitle={t('settings.exportBody')} />
      <div className="space-y-4 px-5 pb-5">
        <ul className="grid gap-x-6 gap-y-1.5 text-sm text-ink-2 sm:grid-cols-2">
          {(['patients', 'appointments', 'plans', 'chart', 'billing', 'notes', 'messages', 'staff'] as const).map((k) => (
            <li key={k} className="flex items-center gap-2"><FileArchive className="size-3.5 shrink-0 text-ink-3" />{t(`settings.exportContents.${k}`)}</li>
          ))}
        </ul>
        <Switch checked={includeDocuments} onChange={setIncludeDocuments} label={t('settings.exportDocuments')} />
        <p className="text-xs text-ink-3">{t('settings.exportNote')}</p>
        <Button icon={<Download className="size-4" />} loading={busy} onClick={() => void run()}>{t('settings.exportButton')}</Button>
      </div>
    </Card>
  )
}
