import { toast } from 'sonner'
import { errorMessage, http } from '@/lib/api'
import { saveBlob } from '@/lib/utils'
import type { ImportKind, ImportOptions } from '@/lib/types'

export const defaultOptions: ImportOptions = {
  dateFormat: 'dmy', allowMissingPhone: false, keepFileNumbers: true, duplicateAction: 'skip', possibleDuplicateAction: 'create',
  assumePastAppointmentsCompleted: false, defaultDentistId: null, defaultDurationMinutes: 30,
}

/** Reads the file name the server suggests in Content-Disposition. */
function fileNameOf(header: string | undefined, fallback: string) {
  const utf8 = header?.match(/filename\*=UTF-8''([^;]+)/i)?.[1]
  if (utf8) return decodeURIComponent(utf8)
  return header?.match(/filename="?([^";]+)"?/i)?.[1] ?? fallback
}

async function download(url: string, fallback: string, params?: object) {
  try {
    const r = await http.get(url, { params, responseType: 'blob' })
    saveBlob(r.data as Blob, fileNameOf(r.headers['content-disposition'] as string | undefined, fallback))
  } catch (e) {
    toast.error(errorMessage(e))
  }
}

export const downloadTemplate = (kind: ImportKind, lang: 'en' | 'ar') =>
  download(`/import/templates/${kind}`, `tahona-${kind.toLowerCase()}-template.csv`, { lang })

export const downloadReport = (batchId: string, problemsOnly: boolean) =>
  download(`/import/batches/${batchId}/report`, 'import-report.csv', { problemsOnly })
