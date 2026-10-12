import { useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { useMutation, useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import {
  AlertTriangle, ArrowLeft, CalendarPlus, ClipboardPlus, CreditCard, Mail, MapPin, MessageCircle, MoreHorizontal, Pencil, Phone, ShieldCheck, Sparkles, Trash2,
} from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { date, money, relative } from '@/lib/format'
import { displayName, Permissions } from '@/lib/utils'
import type { PatientDetail, RecoveryPatient } from '@/lib/types'
import { useAuth } from '@/auth/AuthProvider'
import { Avatar, Badge, Card, CardHeader, ErrorState, KeyValue, Skeleton, StatCard, Tabs } from '@/components/ui/primitives'
import { Button } from '@/components/ui/Button'
import { ConfirmDialog } from '@/components/ui/Dialog'
import { Menu, MenuItem } from '@/components/ui/Menu'
import { IssueBadge, PriorityBadge } from '@/components/StatusBadge'
import { PatientChart } from '@/features/chart/PatientChart'
import { AppointmentDialog, type AppointmentDraft } from '@/features/appointments/AppointmentDialog'
import { PlanFormDialog } from '@/features/plans/PlanFormDialog'
import { RecordPaymentDialog } from '@/features/billing/RecordPaymentDialog'
import { InvoiceFormDialog } from '@/features/billing/InvoiceFormDialog'
import { SendMessageDialog } from '@/features/messages/SendMessageDialog'
import { ContactDialog } from '@/features/recovery/ContactDialog'
import { PatientFormDialog } from './PatientFormDialog'
import { AppointmentsTab, CommunicationsTab, DocumentsTab, HistoryTab, InvoicesTab, NotesTab, PaymentsTab, PlansTab, TimelineCard } from './PatientTabs'
import { recoveryAction } from '@/features/recovery/recoveryText'

type Tab = 'overview' | 'appointments' | 'chart' | 'plans' | 'history' | 'invoices' | 'payments' | 'notes' | 'documents' | 'communications'

export default function PatientProfilePage() {
  const { id = '' } = useParams()
  const { t } = useTranslation()
  const { can } = useAuth()
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const tab = (params.get('tab') as Tab) ?? 'overview'
  const setTab = (v: Tab) => setParams({ tab: v }, { replace: true })

  const [editing, setEditing] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const [booking, setBooking] = useState<AppointmentDraft | null>(null)
  const [planOpen, setPlanOpen] = useState(false)
  const [payInvoice, setPayInvoice] = useState<string | undefined | null>(null)
  const [invoiceOpen, setInvoiceOpen] = useState(false)
  const [messageOpen, setMessageOpen] = useState(false)
  const [contact, setContact] = useState<RecoveryPatient | null>(null)

  const q = useQuery({ queryKey: ['patient', id], queryFn: () => api.get<PatientDetail>(`/patients/${id}`) })
  const recovery = useQuery({
    queryKey: ['recovery', 'patient', id],
    queryFn: () => api.get<RecoveryPatient | ''>(`/patients/${id}/recovery`),
    enabled: can(Permissions.RecoveryView),
    select: (d) => (d ? d : null),
  })
  const remove = useMutation({
    mutationFn: () => api.delete(`/patients/${id}`),
    onSuccess: () => { toast.success(t('common.deleted')); navigate('/patients') },
    onError: (e) => { toast.error(errorMessage(e)); setDeleting(false) },
  })

  if (q.error) return <ErrorState error={q.error} onRetry={() => void q.refetch()} />
  if (q.isLoading || !q.data) return <div className="space-y-4"><Skeleton className="h-36" /><Skeleton className="h-24" /><Skeleton className="h-96" /></div>

  const p = q.data
  // Show the name in the other language as a secondary line.
  const shown = displayName(p.fullName, p.fullNameAr)
  const otherName = p.fullNameAr ? (shown === p.fullName ? p.fullNameAr : p.fullName) : null
  const picked = { id: p.id, fullName: p.fullName, fullNameAr: p.fullNameAr, phone: p.phone }
  const medicalAlert = [p.allergies && `${t('patients.fields.allergies')}: ${p.allergies}`, p.medicalConditions && `${t('patients.fields.conditions')}: ${p.medicalConditions}`]
    .filter(Boolean).join(' · ')
  const wa = (p.whatsAppNumber || p.phone).replace(/\D/g, '').replace(/^0/, '962')
  const book = () => setBooking({ patient: picked, dentistId: p.primaryDentistId })
  const r = recovery.data

  return (
    <div className="space-y-6">
      <Link to="/patients" className="inline-flex items-center gap-1.5 text-sm text-ink-3 hover:text-ink">
        <ArrowLeft className="size-4 rtl:rotate-180" />{t('patients.title')}
      </Link>

      <Card className="p-5">
        <div className="flex flex-wrap items-start gap-5">
          <Avatar name={p.fullName} size="lg" />
          <div className="min-w-0 flex-1">
            <div className="flex flex-wrap items-center gap-2">
              <h1 className="text-xl font-semibold text-ink">{displayName(p.fullName, p.fullNameAr)}</h1>
              <Badge tone={p.status === 'Active' ? 'good' : 'neutral'} dot>{t(`enum.PatientStatus.${p.status}`)}</Badge>
              {p.insuranceProvider && <Badge tone="info"><ShieldCheck className="size-3" />{p.insuranceProvider}{p.insuranceCoveragePercent != null && ` ${p.insuranceCoveragePercent}%`}</Badge>}
            </div>
            <p className="mt-1 text-sm text-ink-3">
              {otherName && `${otherName} · `}
              {p.fileNumber}{p.age != null && ` · ${t('patients.years', { count: p.age })}`}{p.gender && ` · ${t(`enum.Gender.${p.gender}`)}`}
              {p.primaryDentistName && ` · ${p.primaryDentistName}`}
            </p>
            <div className="mt-3 flex flex-wrap gap-x-5 gap-y-1.5 text-sm text-ink-2">
              <a href={`tel:${p.phone}`} className="inline-flex items-center gap-1.5 hover:text-ink"><Phone className="size-3.5" /><span className="ltr">{p.phone}</span></a>
              <a href={`https://wa.me/${wa}`} target="_blank" rel="noopener noreferrer" className="inline-flex items-center gap-1.5 hover:text-ink"><MessageCircle className="size-3.5" />WhatsApp</a>
              {p.email && <a href={`mailto:${p.email}`} className="inline-flex items-center gap-1.5 hover:text-ink"><Mail className="size-3.5" />{p.email}</a>}
              {(p.address || p.city) && <span className="inline-flex items-center gap-1.5"><MapPin className="size-3.5" />{[p.address, p.city].filter(Boolean).join(', ')}</span>}
            </div>
          </div>
          <div className="flex flex-wrap gap-2">
            {can(Permissions.AppointmentsManage) && <Button icon={<CalendarPlus className="size-4" />} onClick={book}>{t('patients.bookAppointment')}</Button>}
            {can(Permissions.PaymentsManage) && p.stats.outstandingBalance > 0 && (
              <Button variant="outline" icon={<CreditCard className="size-4" />} onClick={() => setPayInvoice(undefined)}>{t('patients.recordPayment')}</Button>
            )}
            <Menu trigger={({ toggle }) => <Button variant="outline" size="icon" onClick={toggle} aria-label={t('common.actions')}><MoreHorizontal className="size-4" /></Button>}>
              {(close) => (
                <>
                  {can(Permissions.PatientsEdit) && <MenuItem icon={<Pencil />} onClick={() => { close(); setEditing(true) }}>{t('common.edit')}</MenuItem>}
                  {can(Permissions.TreatmentManage) && <MenuItem icon={<ClipboardPlus />} onClick={() => { close(); setPlanOpen(true) }}>{t('plans.new')}</MenuItem>}
                  {can(Permissions.MessagesSend) && <MenuItem icon={<MessageCircle />} onClick={() => { close(); setMessageOpen(true) }}>{t('patients.sendMessage')}</MenuItem>}
                  {can(Permissions.InvoicesManage) && <MenuItem icon={<CreditCard />} onClick={() => { close(); setInvoiceOpen(true) }}>{t('billing.newInvoice')}</MenuItem>}
                  {can(Permissions.PatientsDelete) && <MenuItem danger icon={<Trash2 />} onClick={() => { close(); setDeleting(true) }}>{t('common.delete')}</MenuItem>}
                </>
              )}
            </Menu>
          </div>
        </div>

        {medicalAlert && (
          <div role="alert" className="mt-4 flex items-start gap-2 rounded-xl bg-critical-soft px-4 py-3 text-sm text-critical-ink">
            <AlertTriangle className="mt-0.5 size-4 shrink-0" /><span><strong>{t('patients.medicalAlert')}:</strong> {medicalAlert}</span>
          </div>
        )}

        {r && (
          <div className="mt-4 flex flex-wrap items-center gap-3 rounded-xl bg-recovery-soft px-4 py-3">
            <span className="grid size-9 place-items-center rounded-lg bg-recovery-gradient text-sm font-bold text-white tabular">{r.score.score}</span>
            <div className="min-w-0 flex-1">
              <p className="flex flex-wrap items-center gap-2 text-sm font-medium text-recovery-ink">
                <Sparkles className="size-4" />{t('patients.recoveryFlag')} · {money(r.totalValue)}
                {r.issues.map((i) => <IssueBadge key={i.sourceEntityId + i.issueType} issue={i.issueType} />)}
              </p>
              <p className="truncate text-xs text-ink-2">{recoveryAction(r, t)}{r.lastContactedAt && ` · ${t('recovery.lastContacted', { when: relative(r.lastContactedAt) })}`}</p>
            </div>
            <PriorityBadge priority={r.score.priority} />
            {can(Permissions.RecoveryManage) && <Button size="sm" variant="recovery" icon={<MessageCircle className="size-3.5" />} onClick={() => setContact(r)}>{t('recovery.contact')}</Button>}
          </div>
        )}
      </Card>

      <div className="grid grid-cols-2 gap-4 md:grid-cols-3 xl:grid-cols-6">
        <StatCard label={t('patients.stats.visits')} value={p.stats.completedVisits} hint={p.lastVisitAt ? relative(p.lastVisitAt) : '—'} />
        <StatCard label={t('patients.nextAppointment')} value={p.stats.nextAppointmentAt ? date(p.stats.nextAppointmentAt) : '—'} />
        <StatCard label={t('patients.stats.openValue')} value={money(p.stats.openTreatmentValue)} hint={t('dashboard.openPlans', { count: p.stats.activePlans })} />
        <StatCard label={t('patients.stats.billed')} value={money(p.stats.totalBilled)} />
        <StatCard label={t('patients.stats.paid')} value={money(p.stats.totalPaid)} />
        <StatCard label={t('patients.stats.balance')} value={<span className={p.stats.outstandingBalance > 0 ? 'text-critical-ink' : undefined}>{money(p.stats.outstandingBalance)}</span>}
          hint={`${t('patients.stats.noShows')}: ${p.stats.noShows}`} />
      </div>

      <Tabs<Tab>
        value={tab}
        onChange={setTab}
        items={[
          { value: 'overview', label: t('patients.tabs.overview') },
          { value: 'appointments', label: t('patients.tabs.appointments'), hidden: !can(Permissions.AppointmentsView) },
          { value: 'chart', label: t('patients.tabs.chart'), hidden: !can(Permissions.ChartView) },
          { value: 'plans', label: t('patients.tabs.plans'), hidden: !can(Permissions.TreatmentView) },
          { value: 'history', label: t('patients.tabs.history'), hidden: !can(Permissions.TreatmentView) },
          { value: 'invoices', label: t('patients.tabs.invoices'), hidden: !can(Permissions.BillingView) },
          { value: 'payments', label: t('patients.tabs.payments'), hidden: !can(Permissions.BillingView) },
          { value: 'notes', label: t('patients.tabs.notes') },
          { value: 'documents', label: t('patients.tabs.documents'), hidden: !can(Permissions.DocumentsView) },
          { value: 'communications', label: t('patients.tabs.communications'), hidden: !can(Permissions.MessagesView) },
        ]}
      />

      {tab === 'overview' && (
        <div className="grid gap-6 xl:grid-cols-[1fr_380px]">
          <TimelineCard patientId={id} />
          <div className="space-y-6">
            <Card>
              <CardHeader title={t('patients.sections.medical')} />
              <div className="divide-y divide-line px-5 pb-3">
                <KeyValue label={t('patients.fields.allergies')} value={p.allergies} />
                <KeyValue label={t('patients.fields.conditions')} value={p.medicalConditions} />
                <KeyValue label={t('patients.fields.medications')} value={p.currentMedications} />
                {p.medicalNotes && <p className="py-2 text-sm text-ink-2">{p.medicalNotes}</p>}
              </div>
            </Card>
            <Card>
              <CardHeader title={t('patients.sections.personal')} />
              <div className="divide-y divide-line px-5 pb-3">
                <KeyValue label={t('patients.fields.dateOfBirth')} value={p.dateOfBirth ? date(p.dateOfBirth) : null} />
                <KeyValue label={t('patients.fields.nationalId')} value={p.nationalId} />
                <KeyValue label={t('patients.fields.preferredLanguage')} value={p.preferredLanguage === 'ar' ? 'العربية' : 'English'} />
                <KeyValue label={t('patients.fields.referral')} value={p.referralSource} />
                <KeyValue label={t('patients.registered')} value={date(p.registeredAt)} />
                <KeyValue label={t('patients.sections.emergency')} value={p.emergencyContactName && `${p.emergencyContactName}${p.emergencyContactRelation ? ` (${p.emergencyContactRelation})` : ''}`} />
                <KeyValue label={t('patients.fields.emergencyPhone')} value={p.emergencyContactPhone && <span className="ltr">{p.emergencyContactPhone}</span>} />
                <KeyValue label={t('patients.fields.policyNumber')} value={p.insurancePolicyNumber} />
              </div>
            </Card>
          </div>
        </div>
      )}
      {tab === 'appointments' && <AppointmentsTab patientId={id} onBook={book} />}
      {tab === 'chart' && <PatientChart patient={picked} />}
      {tab === 'plans' && <PlansTab patientId={id} onNew={() => setPlanOpen(true)} />}
      {tab === 'history' && <HistoryTab patientId={id} />}
      {tab === 'invoices' && <InvoicesTab patientId={id} onNew={() => setInvoiceOpen(true)} onPay={(inv) => setPayInvoice(inv)} />}
      {tab === 'payments' && <PaymentsTab patientId={id} onPay={() => setPayInvoice(undefined)} />}
      {tab === 'notes' && <NotesTab patientId={id} />}
      {tab === 'documents' && <DocumentsTab patientId={id} />}
      {tab === 'communications' && <CommunicationsTab patientId={id} onSend={() => setMessageOpen(true)} />}

      <PatientFormDialog open={editing} onClose={() => setEditing(false)} patient={p} />
      <AppointmentDialog open={!!booking} draft={booking ?? {}} onClose={() => setBooking(null)} />
      <PlanFormDialog open={planOpen} onClose={() => setPlanOpen(false)} patient={picked} onSaved={(pid) => navigate(`/treatment-plans/${pid}`)} />
      <RecordPaymentDialog open={payInvoice !== null} onClose={() => setPayInvoice(null)} patient={picked} invoiceId={payInvoice ?? undefined} />
      <InvoiceFormDialog open={invoiceOpen} onClose={() => setInvoiceOpen(false)} patient={picked} onSaved={(iid) => navigate(`/invoices/${iid}`)} />
      <SendMessageDialog open={messageOpen} onClose={() => setMessageOpen(false)} patient={picked} />
      <ContactDialog patient={contact} onClose={() => setContact(null)} />
      <ConfirmDialog open={deleting} onClose={() => setDeleting(false)} onConfirm={() => remove.mutate()} danger loading={remove.isPending}
        title={t('common.delete')} body={t('patients.deleteConfirm')} confirmLabel={t('common.delete')} />
    </div>
  )
}
