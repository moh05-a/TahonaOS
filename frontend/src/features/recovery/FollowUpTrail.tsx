import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { ArrowUpRight, Bot, MessageCircle, Phone, Reply } from 'lucide-react'
import { api } from '@/lib/api'
import { date, dateTime, money, relative } from '@/lib/format'
import type { FollowUpTrail as Trail } from '@/lib/types'
import { ErrorState, Skeleton } from '@/components/ui/primitives'
import { FollowUpStatusBadge, IssueBadge } from '@/components/StatusBadge'

/**
 * The record behind a patient's place on the list: for each follow-up, the event that raised it and when,
 * the contacts made, and the outcome that was recorded.
 */
export function FollowUpTrail({ patientId }: { patientId: string }) {
  const { t } = useTranslation()
  const q = useQuery({ queryKey: ['recovery', 'history', patientId], queryFn: () => api.get<Trail[]>(`/recovery/patients/${patientId}/history`) })

  if (q.error) return <ErrorState error={q.error} onRetry={() => void q.refetch()} />
  if (!q.data) return <Skeleton className="h-24" />
  if (q.data.length === 0) return <p className="text-sm text-ink-3">{t('recovery.trail.none')}</p>

  return (
    <ol className="space-y-3">
      {q.data.map((f) => (
        <li key={f.id} className="rounded-xl border border-line p-3">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <IssueBadge issue={f.issueType} />
            <FollowUpStatusBadge status={f.status} />
          </div>

          <dl className="mt-3 space-y-1.5 text-sm">
            <Line label={t('recovery.trail.source')}>
              {f.sourceLink ? (
                <Link to={f.sourceLink} className="inline-flex items-center gap-1 text-primary-ink hover:underline">
                  {f.sourceLabel ?? t('recovery.trail.openRecord')}<ArrowUpRight className="size-3 rtl:-scale-x-100" />
                </Link>
              ) : (f.sourceLabel ?? '—')}
            </Line>
            <Line label={t('recovery.trail.event')}>{f.eventAt ? `${date(f.eventAt)} (${relative(f.eventAt)})` : '—'}</Line>
            <Line label={t('recovery.trail.listed')}>{date(f.identifiedAt)}</Line>
            <Line label={t('recovery.trail.value')}>{money(f.estimatedValue)}</Line>
            {f.bookedAppointmentAt && <Line label={t('recovery.trail.booked')}>{dateTime(f.bookedAppointmentAt)}</Line>}
            {f.collectedAfterFollowUp > 0 && <Line label={t('recovery.trail.collected')}><span className="font-medium text-good-ink">{money(f.collectedAfterFollowUp)}</span></Line>}
            {f.outcome && <Line label={t('recovery.trail.outcome')}>{f.outcome}{f.resolvedAt && <span className="text-ink-3"> · {date(f.resolvedAt)}</span>}</Line>}
            {f.assignedTo && <Line label={t('recovery.trail.owner')}>{f.assignedTo}</Line>}
          </dl>

          <p className="mt-3 text-xs font-medium text-ink-3">{t('recovery.trail.contacts', { count: f.contacts.length })}</p>
          {f.contacts.length > 0 && (
            <ul className="mt-1.5 space-y-1.5">
              {f.contacts.map((c) => (
                <li key={c.messageId} className="flex gap-2 text-xs text-ink-2">
                  <span className="mt-0.5 shrink-0 text-ink-3">
                    {c.direction === 'Inbound' ? <Reply className="size-3.5" /> : c.isAutomated ? <Bot className="size-3.5" /> : c.channel === 'Phone' ? <Phone className="size-3.5" /> : <MessageCircle className="size-3.5" />}
                  </span>
                  <span className="min-w-0">
                    <span className="text-ink-3">{dateTime(c.at)} · {c.direction === 'Inbound' ? t('messages.inbound') : c.isAutomated ? t('messages.automated') : (c.by ?? '—')} · {t(`enum.MessageStatus.${c.status}`)}</span>
                    <span className="block truncate" dir="auto">{c.body}</span>
                  </span>
                </li>
              ))}
            </ul>
          )}
        </li>
      ))}
    </ol>
  )
}

function Line({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex justify-between gap-4">
      <dt className="shrink-0 text-ink-3">{label}</dt>
      <dd className="min-w-0 text-end text-ink" dir="auto">{children}</dd>
    </div>
  )
}
