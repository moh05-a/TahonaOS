import { useEffect, useMemo, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { useNavigate } from 'react-router-dom'
import { useQuery, keepPreviousData } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { CalendarDays, ClipboardList, FileText, Search, User } from 'lucide-react'
import { api } from '@/lib/api'
import { cn } from '@/lib/utils'
import { dateTime, money } from '@/lib/format'
import type { SearchHit, SearchResult } from '@/lib/types'
import { Spinner } from '@/components/ui/primitives'

function useDebounced<T>(value: T, ms: number) {
  const [v, setV] = useState(value)
  useEffect(() => {
    const id = setTimeout(() => setV(value), ms)
    return () => clearTimeout(id)
  }, [value, ms])
  return v
}

const icons = { patient: User, appointment: CalendarDays, plan: ClipboardList, invoice: FileText }

/** Global search (Ctrl/⌘+K). Typing a phone number like 0791234567 finds the patient immediately. */
export function CommandPalette({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const [q, setQ] = useState('')
  const [active, setActive] = useState(0)
  const inputRef = useRef<HTMLInputElement>(null)
  const debounced = useDebounced(q.trim(), 120)

  const search = useQuery({
    queryKey: ['search', debounced],
    queryFn: () => api.get<SearchResult>('/search', { q: debounced }),
    enabled: open && debounced.length >= 2,
    placeholderData: keepPreviousData,
    staleTime: 10_000,
  })

  const groups = useMemo(() => {
    const d = search.data
    if (!d || debounced.length < 2) return []
    return [
      { label: t('nav.patients'), hits: d.patients },
      { label: t('nav.appointments'), hits: d.appointments },
      { label: t('nav.treatmentPlans'), hits: d.treatmentPlans },
      { label: t('nav.invoices'), hits: d.invoices },
    ].filter((g) => g.hits.length > 0)
  }, [search.data, debounced, t])
  const flat = groups.flatMap((g) => g.hits)

  useEffect(() => {
    if (open) {
      setQ('')
      setActive(0)
      requestAnimationFrame(() => inputRef.current?.focus())
    }
  }, [open])
  useEffect(() => { setActive(0) }, [debounced])

  const go = (hit: SearchHit) => {
    onClose()
    navigate(hit.link)
  }

  if (!open) return null

  const meta = (hit: SearchHit) => {
    if (!hit.meta) return null
    if (hit.kind === 'appointment') return dateTime(hit.meta.split('|')[0])
    if (hit.kind === 'plan') return money(Number(hit.meta.split('|')[1]))
    if (hit.kind === 'invoice') {
      const [, total, balance] = hit.meta.split('|')
      return Number(balance) > 0 ? `${money(Number(balance))} / ${money(Number(total))}` : money(Number(total))
    }
    return hit.meta
  }

  let index = -1
  return createPortal(
    <div className="fixed inset-0 z-50 flex items-start justify-center p-4 pt-[10vh]">
      <div className="absolute inset-0 bg-black/40 backdrop-blur-[2px]" onClick={onClose} />
      <div className="relative w-full max-w-xl overflow-hidden rounded-2xl border border-line bg-surface shadow-pop" role="dialog" aria-modal="true">
        <div className="flex items-center gap-3 border-b border-line px-4">
          <Search className="size-4 text-ink-3" />
          <input
            ref={inputRef}
            value={q}
            onChange={(e) => setQ(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Escape') onClose()
              if (e.key === 'ArrowDown') { e.preventDefault(); setActive((a) => Math.min(a + 1, flat.length - 1)) }
              if (e.key === 'ArrowUp') { e.preventDefault(); setActive((a) => Math.max(a - 1, 0)) }
              if (e.key === 'Enter' && flat[active]) go(flat[active])
            }}
            placeholder={t('common.searchPlaceholder')}
            className="h-14 flex-1 bg-transparent text-sm text-ink outline-none placeholder:text-ink-3"
            aria-label={t('common.search')}
          />
          {search.isFetching && <Spinner className="size-4" />}
          <kbd className="rounded border border-line px-1.5 text-[10px] text-ink-3">ESC</kbd>
        </div>
        <div className="max-h-[60vh] overflow-y-auto p-2">
          {debounced.length < 2 ? (
            <p className="px-3 py-6 text-center text-sm text-ink-3">{t('common.searchPlaceholder')}</p>
          ) : search.isError ? (
            <p className="px-3 py-6 text-center text-sm text-critical-ink">{t('common.errorTitle')}</p>
          ) : groups.length === 0 && !search.isFetching ? (
            <p className="px-3 py-6 text-center text-sm text-ink-3">{t('common.noResults')}</p>
          ) : (
            groups.map((g) => (
              <div key={g.label} className="mb-1">
                <p className="px-3 pt-2 pb-1 text-[10px] font-semibold tracking-wider text-ink-3 uppercase">{g.label}</p>
                {g.hits.map((hit) => {
                  index++
                  const i = index
                  const Icon = icons[hit.kind]
                  return (
                    <button
                      key={`${hit.kind}-${hit.id}`}
                      onMouseEnter={() => setActive(i)}
                      onClick={() => go(hit)}
                      className={cn('flex w-full items-center gap-3 rounded-lg px-3 py-2 text-start', active === i ? 'bg-primary-soft' : 'hover:bg-surface-2')}
                    >
                      <span className="grid size-8 shrink-0 place-items-center rounded-lg bg-surface-3 text-ink-2"><Icon className="size-4" /></span>
                      <span className="min-w-0 flex-1">
                        <span className="block truncate text-sm font-medium text-ink">{hit.title}</span>
                        {hit.subtitle && <span className="ltr block truncate text-xs text-ink-3">{hit.subtitle}</span>}
                      </span>
                      <span className="tabular shrink-0 text-xs text-ink-3">{meta(hit)}</span>
                    </button>
                  )
                })}
              </div>
            ))
          )}
        </div>
        {search.data && debounced.length >= 2 && (
          <div className="border-t border-line px-4 py-2 text-[11px] text-ink-3">{search.data.elapsedMs} ms</div>
        )}
      </div>
    </div>,
    document.body,
  )
}
