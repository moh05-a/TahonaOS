import type { TFunction } from 'i18next'
import { money } from '@/lib/format'
import type { RecoveryPatient, RecoveryScore } from '@/lib/types'

/** Recommended next step, composed client-side so it follows the UI language. */
export function recoveryAction(p: RecoveryPatient, t: TFunction) {
  const issue = p.issues.find((i) => i.issueType === p.primaryIssue) ?? p.issues[0]
  if (!issue) return p.recommendedAction
  const days = Math.max(0, Math.floor((Date.now() - new Date(issue.triggeredAt).getTime()) / 86_400_000))
  return t(`recovery.action.${issue.issueType}`, { treatment: p.suggestedTreatment ?? '', days, value: money(issue.value) })
}

/** "Why this score" bullet points in the UI language. */
export function recoveryReasons(s: RecoveryScore, t: TFunction) {
  const f = s.facts
  const out: string[] = []
  if (f.totalValue > 0) out.push(t('recovery.reason.value', { value: money(f.totalValue) }))
  out.push(t('recovery.reason.sinceVisit', { count: f.daysSinceLastVisit }))
  if (f.daysOverdue > 0) out.push(t('recovery.reason.overdue', { count: f.daysOverdue }))
  if (f.issueCount > 1) out.push(t('recovery.reason.issues', { count: f.issueCount }))
  if (f.hasAcceptedPlan) out.push(t('recovery.reason.accepted'))
  if (f.noShows > 0) out.push(t('recovery.reason.noShows', { count: f.noShows }))
  out.push(t('recovery.reason.likelihood', { label: t(`recovery.likelihoodLabel.${s.likelihoodLabel}`), count: f.completedVisits }))
  return out
}
