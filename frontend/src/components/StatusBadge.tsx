import { AlertCircle, ArrowUp, CheckCircle2, Clock, Flame, Minus } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Badge, type Tone } from '@/components/ui/primitives'
import type {
  AppointmentStatus, FollowUpStatus, InvoiceStatus, MessageStatus, PriorityLevel, RecoveryIssueType, TreatmentItemStatus, TreatmentPlanStatus,
} from '@/lib/types'

const appointmentTone: Record<AppointmentStatus, Tone> = {
  Scheduled: 'neutral', Confirmed: 'info', Arrived: 'primary', InTreatment: 'recovery', Completed: 'good', Cancelled: 'neutral', NoShow: 'critical',
}
export const AppointmentStatusBadge = ({ status }: { status: AppointmentStatus }) => {
  const { t } = useTranslation()
  return <Badge tone={appointmentTone[status]} dot>{t(`enum.AppointmentStatus.${status}`)}</Badge>
}

const planTone: Record<TreatmentPlanStatus, Tone> = {
  Draft: 'neutral', Presented: 'info', Accepted: 'primary', InProgress: 'recovery', PartiallyCompleted: 'warning', Completed: 'good', Rejected: 'critical',
}
export const PlanStatusBadge = ({ status }: { status: TreatmentPlanStatus }) => {
  const { t } = useTranslation()
  return <Badge tone={planTone[status]} dot>{t(`enum.TreatmentPlanStatus.${status}`)}</Badge>
}

const itemTone: Record<TreatmentItemStatus, Tone> = { Planned: 'neutral', Scheduled: 'info', Completed: 'good', Cancelled: 'critical' }
export const ItemStatusBadge = ({ status }: { status: TreatmentItemStatus }) => {
  const { t } = useTranslation()
  return <Badge tone={itemTone[status]}>{t(`enum.TreatmentItemStatus.${status}`)}</Badge>
}

const invoiceTone: Record<InvoiceStatus, Tone> = { Draft: 'neutral', Issued: 'info', PartiallyPaid: 'warning', Paid: 'good', Void: 'neutral', Refunded: 'serious' }
export const InvoiceStatusBadge = ({ status, overdue }: { status: InvoiceStatus; overdue?: boolean }) => {
  const { t } = useTranslation()
  if (overdue) return <Badge tone="critical" dot>{t('billing.overdue')}</Badge>
  return <Badge tone={invoiceTone[status]} dot>{t(`enum.InvoiceStatus.${status}`)}</Badge>
}

/** Priority always carries an icon + label so colour is never the only signal. */
export const PriorityBadge = ({ priority }: { priority: PriorityLevel }) => {
  const { t } = useTranslation()
  const map: Record<PriorityLevel, { tone: Tone; icon: React.ReactNode }> = {
    Urgent: { tone: 'critical', icon: <Flame className="size-3" /> },
    High: { tone: 'serious', icon: <ArrowUp className="size-3" /> },
    Medium: { tone: 'warning', icon: <Minus className="size-3" /> },
    Low: { tone: 'neutral', icon: <Clock className="size-3" /> },
  }
  return (
    <Badge tone={map[priority].tone} className="uppercase tracking-wide">
      {map[priority].icon}
      {t(`enum.PriorityLevel.${priority}`)}
    </Badge>
  )
}

const issueTone: Record<RecoveryIssueType, Tone> = {
  TreatmentNotStarted: 'recovery', TreatmentUnfinished: 'serious', MissedAppointment: 'critical', OutstandingBalance: 'warning',
  OverdueFollowUp: 'info', RecallDue: 'primary',
}
export const IssueBadge = ({ issue }: { issue: RecoveryIssueType }) => {
  const { t } = useTranslation()
  return <Badge tone={issueTone[issue]}>{t(`enum.RecoveryIssueType.${issue}`)}</Badge>
}

const followTone: Record<FollowUpStatus, Tone> = {
  Pending: 'neutral', Contacted: 'info', Booked: 'primary', Recovered: 'good', NoResponse: 'warning', Dismissed: 'neutral', Resolved: 'neutral',
}
export const FollowUpStatusBadge = ({ status }: { status: FollowUpStatus }) => {
  const { t } = useTranslation()
  return <Badge tone={followTone[status]}>{t(`enum.FollowUpStatus.${status}`)}</Badge>
}

export const MessageStatusBadge = ({ status }: { status: MessageStatus }) => {
  const { t } = useTranslation()
  const tone: Tone = status === 'Failed' ? 'critical' : status === 'ManualPending' ? 'warning' : status === 'Read' || status === 'Delivered' ? 'good' : 'neutral'
  const icon = status === 'Failed' ? <AlertCircle className="size-3" /> : status === 'Read' || status === 'Delivered' ? <CheckCircle2 className="size-3" /> : null
  return <Badge tone={tone}>{icon}{t(`enum.MessageStatus.${status}`)}</Badge>
}
