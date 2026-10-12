import { useEffect, useRef, useState } from 'react'
import { useQuery, keepPreviousData } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { Search, X } from 'lucide-react'
import { api } from '@/lib/api'
import { cn, displayName } from '@/lib/utils'
import type { Paged, PatientListItem } from '@/lib/types'
import { Avatar, Spinner } from '@/components/ui/primitives'

export interface PickedPatient { id: string; fullName: string; fullNameAr?: string; phone?: string }

/** Async patient search (name in English/Arabic, phone or file number). */
export function PatientPicker({ value, onChange, invalid, disabled }: {
  value: PickedPatient | null; onChange: (p: PickedPatient | null) => void; invalid?: boolean; disabled?: boolean
}) {
  const { t } = useTranslation()
  const [q, setQ] = useState('')
  const [debounced, setDebounced] = useState('')
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const id = setTimeout(() => setDebounced(q.trim()), 150)
    return () => clearTimeout(id)
  }, [q])
  useEffect(() => {
    const onDown = (e: MouseEvent) => !ref.current?.contains(e.target as Node) && setOpen(false)
    document.addEventListener('mousedown', onDown)
    return () => document.removeEventListener('mousedown', onDown)
  }, [])

  const { data, isFetching } = useQuery({
    queryKey: ['patient-picker', debounced],
    queryFn: () => api.get<Paged<PatientListItem>>('/patients', { search: debounced, pageSize: 8, sort: 'lastVisit', desc: true }),
    enabled: open && debounced.length >= 2,
    placeholderData: keepPreviousData,
  })

  if (value) {
    return (
      <div className="flex h-9 items-center gap-2 rounded-lg border border-line bg-surface-2 px-2">
        <Avatar name={value.fullName} size="sm" />
        <span className="min-w-0 flex-1 truncate text-sm font-medium text-ink">{displayName(value.fullName, value.fullNameAr)}</span>
        {value.phone && <span className="ltr text-xs text-ink-3">{value.phone}</span>}
        {!disabled && (
          <button type="button" onClick={() => onChange(null)} className="rounded p-1 text-ink-3 hover:bg-surface-3" aria-label={t('common.reset')}>
            <X className="size-3.5" />
          </button>
        )}
      </div>
    )
  }

  return (
    <div ref={ref} className="relative">
      <div className={cn('flex h-9 items-center gap-2 rounded-lg border bg-surface px-3', invalid ? 'border-critical' : 'border-line focus-within:border-primary')}>
        <Search className="size-4 text-ink-3" />
        <input
          value={q}
          onChange={(e) => { setQ(e.target.value); setOpen(true) }}
          onFocus={() => setOpen(true)}
          placeholder={t('appointments.selectPatient')}
          className="h-full flex-1 bg-transparent text-sm text-ink outline-none placeholder:text-ink-3"
          disabled={disabled}
        />
        {isFetching && <Spinner className="size-4" />}
      </div>
      {open && debounced.length >= 2 && (
        <div className="absolute top-full z-50 mt-1 w-full overflow-hidden rounded-xl border border-line bg-surface shadow-pop">
          {data?.items.length ? (
            data.items.map((p) => (
              <button
                type="button"
                key={p.id}
                onClick={() => { onChange({ id: p.id, fullName: p.fullName, fullNameAr: p.fullNameAr, phone: p.phone }); setQ(''); setOpen(false) }}
                className="flex w-full items-center gap-2.5 px-3 py-2 text-start hover:bg-surface-2"
              >
                <Avatar name={p.fullName} size="sm" />
                <span className="min-w-0 flex-1">
                  <span className="block truncate text-sm text-ink">{displayName(p.fullName, p.fullNameAr)}</span>
                  <span className="block text-xs text-ink-3"><span className="ltr">{p.phone}</span> · {p.fileNumber}</span>
                </span>
              </button>
            ))
          ) : (
            <p className="px-3 py-3 text-sm text-ink-3">{isFetching ? t('common.loading') : t('common.noResults')}</p>
          )}
        </div>
      )}
    </div>
  )
}
