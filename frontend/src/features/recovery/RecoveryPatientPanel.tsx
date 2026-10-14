import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { CalendarPlus, Info, MessageCircle, UserRound } from 'lucide-react'
import { date, money, relative } from '@/lib/format'
import { displayName } from '@/lib/utils'
import type { RecoveryPatient } from '@/lib/types'
import { Dialog } from '@/components/ui/Dialog'
import { Button } from '@/components/ui/Button'
import { Badge, KeyValue, ProgressBar } from '@/components/ui/primitives'
import { FollowUpStatusBadge, IssueBadge, PriorityBadge } from '@/components/StatusBadge'
import { recoveryAction, recoveryReasons } from '@/features/recovery/recoveryText'
import { FollowUpTrail } from './FollowUpTrail'

/** Explains a patient's recovery score: the four weighted components, the reasons, and every open issue. */
export function RecoveryPatientPanel({ patient, onClose, onContact, onBook, canManage }: {
  patient: RecoveryPatient | null; onClose: () => void; onContact: (p: RecoveryPatient) => void; onBook: (p: RecoveryPatient) => void; canManage: boolean
}) {
  const { t } = useTranslation()
  if (!patient) return null
  const s = patient.score

  return (
    <Dialog
      side
      open={!!patient}
      onClose={onClose}
      title={displayName(patient.fullName, patient.fullNameAr)}
      description={<span className="ltr">{patient.phone}</span>}
      footer={
        <>
          <Link to={`/patients/${patient.patientId}`} className="me-auto">
            <Button variant="ghost" icon={<UserRound className="size-4" />}>{t('recovery.viewPatient')}</Button>
          </Link>
          {canManage && (
            <>
              <Button variant="outline" icon={<CalendarPlus className="size-4" />} onClick={() => onBook(patient)}>{t('recovery.book')}</Button>
              <Button variant="recovery" icon={<MessageCircle className="size-4" />} onClick={() => onContact(patient)}>{t('recovery.contact')}</Button>
            </>
          )}
        </>
      }
    >
      <div className="space-y-6">
        <div className="flex items-center gap-4 rounded-2xl bg-recovery-soft p-4">
          <div className="tabular grid size-16 shrink-0 place-items-center rounded-2xl bg-recovery-gradient text-2xl font-bold text-white">{s.score}</div>
          <div className="min-w-0 flex-1 space-y-1.5">
            <div className="flex flex-wrap items-center gap-2">
              <PriorityBadge priority={s.priority} />
              <Badge tone="neutral">{t(`recovery.stage.${patient.stage}`)}</Badge>
            </div>
            <p className="text-2xl font-semibold tracking-tight text-ink tabular">{money(patient.totalValue)}</p>
            <p className="text-xs text-ink-2">{t('recovery.likelihood')}: <span className="font-medium text-ink">{t(`recovery.likelihoodLabel.${s.likelihoodLabel}`)}</span> ({Math.round(s.returnLikelihood * 100)}%)</p>
          </div>
        </div>

        <section>
          <h4 className="mb-2 text-xs font-semibold tracking-wide text-ink-3 uppercase">{t('recovery.recommended')}</h4>
          <p className="text-sm text-ink">{recoveryAction(patient, t)}</p>
        </section>

        <section>
          <h4 className="mb-3 flex items-center gap-1.5 text-xs font-semibold tracking-wide text-ink-3 uppercase">{t('recovery.why')}</h4>
          <div className="space-y-3">
            {s.components.map((c) => (
              <div key={c.name}>
                <div className="mb-1 flex justify-between text-xs">
                  <span className="text-ink-2">{t(`recovery.components.${c.name}` as 'recovery.components.Value')} <span className="text-ink-3">· w{c.weight}</span></span>
                  <span className="tabular font-medium text-ink">+{c.points.toFixed(1)}</span>
                </div>
                <ProgressBar value={c.raw * 100} tone="recovery" />
              </div>
            ))}
          </div>
          <ul className="mt-4 space-y-1 text-sm text-ink-2">
            {recoveryReasons(s, t).map((r) => <li key={r} className="flex gap-2"><span className="mt-2 size-1 shrink-0 rounded-full bg-ink-3" />{r}</li>)}
          </ul>
          <p className="mt-3 flex gap-1.5 text-xs text-ink-3"><Info className="mt-0.5 size-3.5 shrink-0" />{t('recovery.algorithm')}</p>
        </section>

        <section>
          <h4 className="mb-2 text-xs font-semibold tracking-wide text-ink-3 uppercase">{t('recovery.issues')}</h4>
          <div className="space-y-2">
            {patient.issues.map((i) => (
              <div key={i.sourceEntityId + i.issueType} className="rounded-xl border border-line p-3">
                <div className="flex items-start justify-between gap-2">
                  <IssueBadge issue={i.issueType} />
                  <span className="tabular text-sm font-semibold text-ink">{money(i.value)}</span>
                </div>
                <p className="mt-2 text-sm text-ink-2">{i.detail}</p>
                <div className="mt-2 flex flex-wrap items-center gap-2 text-xs text-ink-3">
                  <span>{date(i.triggeredAt)}</span>
                  {i.followUpStatus && <FollowUpStatusBadge status={i.followUpStatus} />}
                  {i.treatmentPlanId && <Link className="text-primary-ink hover:underline" to={`/treatment-plans/${i.treatmentPlanId}`}>{t('nav.treatmentPlans')}</Link>}
                </div>
              </div>
            ))}
          </div>
        </section>

        <section>
          <h4 className="mb-2 text-xs font-semibold tracking-wide text-ink-3 uppercase">{t('recovery.trail.title')}</h4>
          <FollowUpTrail patientId={patient.patientId} />
        </section>

        <section className="divide-y divide-line">
          <KeyValue label={t('patients.lastVisit')} value={patient.lastVisitAt ? `${date(patient.lastVisitAt)} (${relative(patient.lastVisitAt)})` : '—'} />
          <KeyValue label={t('recovery.lastContacted', { when: '' }).trim()} value={patient.lastContactedAt ? relative(patient.lastContactedAt) : '—'} />
          <KeyValue label={t('recovery.attempts', { count: patient.contactAttempts })} value={patient.contactAttempts} />
        </section>
      </div>
    </Dialog>
  )
}
