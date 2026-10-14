import { useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { AlertTriangle, ListChecks, Lock } from 'lucide-react'
import { useAuth } from '@/auth/AuthProvider'
import { api } from '@/lib/api'
import { cn, Permissions } from '@/lib/utils'
import type { OnboardingStatus } from '@/lib/types'

/**
 * Account state every signed-in user should know about: a read-only account, an overdue payment or a trial
 * about to end, and (for administrators) setup steps still open.
 */
export function AccountBanner() {
  const { t } = useTranslation()
  const { user, can } = useAuth()
  const location = useLocation()
  const [now] = useState(() => Date.now())
  const onboarding = useQuery({
    queryKey: ['onboarding'],
    queryFn: () => api.get<OnboardingStatus>('/onboarding'),
    enabled: can(Permissions.SettingsManage),
    staleTime: 60_000,
  })
  if (!user) return null

  const clinic = user.clinic
  const items: { key: string; tone: 'critical' | 'warning' | 'info'; icon: React.ReactNode; title: string; body?: string; action?: React.ReactNode }[] = []

  if (clinic.readOnly) {
    items.push({ key: 'readonly', tone: 'critical', icon: <Lock className="size-4" />, title: t('account.readOnlyTitle'), body: t('account.readOnlyBody') })
  } else if (clinic.subscriptionStatus === 'PastDue') {
    items.push({ key: 'pastdue', tone: 'warning', icon: <AlertTriangle className="size-4" />, title: t('account.pastDue') })
  } else if (clinic.subscriptionStatus === 'Trial' && clinic.trialEndsAt) {
    const days = Math.ceil((new Date(clinic.trialEndsAt).getTime() - now) / 86_400_000)
    if (days <= 0) items.push({ key: 'trial', tone: 'warning', icon: <AlertTriangle className="size-4" />, title: t('account.trialEnded') })
    else if (days <= 7) items.push({ key: 'trial', tone: 'info', icon: <AlertTriangle className="size-4" />, title: t('account.trialEnds', { days }) })
  }

  const status = onboarding.data
  if (status && !status.completed && location.pathname !== '/onboarding' && location.pathname !== '/import') {
    items.push({
      key: 'setup', tone: 'info', icon: <ListChecks className="size-4" />, title: t('account.setupTitle'),
      body: t('account.setupBody', { done: status.doneCount, total: status.totalCount }),
      action: <Link to="/onboarding" className="text-sm font-medium whitespace-nowrap underline-offset-2 hover:underline">{t('account.setupOpen')}</Link>,
    })
  }
  if (items.length === 0) return null

  const tones = {
    critical: 'border-critical/30 bg-critical-soft text-critical-ink',
    warning: 'border-warning/30 bg-warning-soft text-warning-ink',
    info: 'border-line bg-surface text-ink-2',
  }
  return (
    <div className="mb-5 space-y-2">
      {items.map((item) => (
        <div key={item.key} role={item.tone === 'info' ? 'status' : 'alert'} className={cn('flex flex-wrap items-center gap-x-3 gap-y-1 rounded-xl border px-4 py-3 text-sm', tones[item.tone])}>
          <span className="shrink-0">{item.icon}</span>
          <p className="min-w-0 flex-1"><span className="font-medium">{item.title}</span>{item.body && <span className="ms-2">{item.body}</span>}</p>
          {item.action}
        </div>
      ))}
    </div>
  )
}
