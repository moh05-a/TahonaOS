import { Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ArrowRight, CheckCircle2, Circle, ListChecks, PartyPopper } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { date, money, number } from '@/lib/format'
import { cn } from '@/lib/utils'
import type { OnboardingStatus, OnboardingStep } from '@/lib/types'
import { Button } from '@/components/ui/Button'
import { Card, CardHeader, ErrorState, KeyValue, LoadingBlock, PageHeader, ProgressBar } from '@/components/ui/primitives'

/** The switching checklist. Steps the system can observe tick themselves; the rest are confirmed by the clinic. */
export default function OnboardingPage() {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const q = useQuery({ queryKey: ['onboarding'], queryFn: () => api.get<OnboardingStatus>('/onboarding') })

  const update = (data: OnboardingStatus) => qc.setQueryData(['onboarding'], data)
  const confirm = useMutation({
    mutationFn: (v: { key: string; confirmed: boolean }) => api.put<OnboardingStatus>(`/onboarding/steps/${v.key}`, { confirmed: v.confirmed }),
    onSuccess: update,
    onError: (e) => toast.error(errorMessage(e)),
  })
  const complete = useMutation({
    mutationFn: (reopen: boolean) => api.post<OnboardingStatus>(reopen ? '/onboarding/reopen' : '/onboarding/complete'),
    onSuccess: update,
    onError: (e) => toast.error(errorMessage(e)),
  })

  if (q.error) return <ErrorState error={q.error} onRetry={() => void q.refetch()} />
  if (!q.data) return <LoadingBlock rows={8} />
  const status = q.data
  const f = status.figures

  return (
    <div className="space-y-6">
      <PageHeader icon={<ListChecks className="size-6 text-primary" />} title={t('onboarding.title')} subtitle={t('onboarding.subtitle')} />

      <div className="grid gap-6 lg:grid-cols-[1fr_340px]">
        <Card>
          <div className="border-b border-line px-5 py-4">
            <div className="flex items-center justify-between gap-3 text-sm">
              <span className="font-medium text-ink">{status.completed ? t('onboarding.completed') : t('onboarding.progress', { done: status.doneCount, total: status.totalCount })}</span>
              {status.completed && status.completedAt && <span className="text-ink-3">{t('onboarding.completedOn', { date: date(status.completedAt) })}</span>}
            </div>
            <ProgressBar className="mt-3" tone="good" value={(status.doneCount / status.totalCount) * 100} />
          </div>
          <ol className="divide-y divide-line">
            {status.steps.map((step, index) => (
              <StepRow key={step.key} step={step} index={index + 1} busy={confirm.isPending || complete.isPending}
                onConfirm={(confirmed) => step.key === 'goLive' ? complete.mutate(!confirmed) : confirm.mutate({ key: step.key, confirmed })} />
            ))}
          </ol>
          {status.completed && (
            <div className="flex flex-wrap items-center gap-3 border-t border-line px-5 py-4 text-sm text-ink-2">
              <PartyPopper className="size-4 text-good" />
              <span className="flex-1">{t('onboarding.completed')}</span>
              <Button variant="outline" size="sm" loading={complete.isPending} onClick={() => complete.mutate(true)}>{t('onboarding.reopen')}</Button>
            </div>
          )}
        </Card>

        <div className="space-y-6">
          <Card>
            <CardHeader title={t('onboarding.figuresTitle')} />
            <div className="divide-y divide-line px-5 pb-4">
              <KeyValue label={t('onboarding.figures.patients')} value={number(f.patients)} />
              <KeyValue label={t('onboarding.figures.importedPatients')} value={number(f.importedPatients)} />
              <KeyValue label={t('onboarding.figures.upcoming')} value={number(f.upcomingAppointments)} />
              <KeyValue label={t('onboarding.figures.outstanding')} value={money(f.outstandingBalance)} />
              <KeyValue label={t('onboarding.figures.openingBalance')} value={money(f.importedOpeningBalance)} />
              <KeyValue label={t('onboarding.figures.team')} value={`${f.dentists} / ${f.staffUsers}`} />
            </div>
          </Card>
          <Card className="p-5">
            <h2 className="text-sm font-semibold text-ink">{t('onboarding.parallelTitle')}</h2>
            <p className="mt-2 text-sm leading-relaxed text-ink-2">{t('onboarding.parallelBody')}</p>
          </Card>
        </div>
      </div>
    </div>
  )
}

function StepRow({ step, index, busy, onConfirm }: { step: OnboardingStep; index: number; busy: boolean; onConfirm: (confirmed: boolean) => void }) {
  const { t } = useTranslation()
  const isGoLive = step.key === 'goLive'
  return (
    <li className="flex gap-4 px-5 py-4">
      <span className="mt-0.5 shrink-0" aria-hidden>
        {step.done ? <CheckCircle2 className="size-5 text-good" /> : <Circle className="size-5 text-ink-3" />}
      </span>
      <div className="min-w-0 flex-1">
        <p className={cn('text-sm font-medium', step.done ? 'text-ink-2' : 'text-ink')}>
          <span className="tabular me-1.5 text-ink-3">{index}.</span>{t(`onboarding.steps.${step.key}.title`)}
          <span className="sr-only"> — {step.done ? t('common.yes') : t('common.no')}</span>
        </p>
        <p className="mt-1 text-sm text-ink-3">{t(`onboarding.steps.${step.key}.body`)}</p>
        <div className="mt-3 flex flex-wrap items-center gap-2">
          {!isGoLive && (
            <Link to={step.link}>
              <Button variant="outline" size="sm" icon={<ArrowRight className="size-3.5 rtl:rotate-180" />}>{t('onboarding.open')}</Button>
            </Link>
          )}
          {step.kind === 'manual' && !step.done && (
            <Button size="sm" variant={isGoLive ? 'primary' : 'secondary'} disabled={!step.canConfirm || busy} onClick={() => onConfirm(true)}
              title={step.canConfirm ? undefined : t('onboarding.needsEarlier')}>
              {isGoLive ? t('onboarding.complete') : t('onboarding.confirm')}
            </Button>
          )}
          {step.kind === 'manual' && step.done && !isGoLive && (
            <Button size="sm" variant="ghost" disabled={busy} onClick={() => onConfirm(false)}>{t('onboarding.undo')}</Button>
          )}
          {step.kind === 'auto' && !step.done && <span className="text-xs text-ink-3">{t('onboarding.auto')}</span>}
          {step.kind === 'manual' && !step.done && !step.canConfirm && <span className="text-xs text-ink-3">{t('onboarding.needsEarlier')}</span>}
        </div>
      </div>
    </li>
  )
}
