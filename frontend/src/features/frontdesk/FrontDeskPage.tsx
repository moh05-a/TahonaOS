import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { AlertTriangle, CalendarPlus, CircleDollarSign, Clock, ConciergeBell, HeartPulse, MessageCircle, Sparkles, UserPlus } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { money, time, weekday } from '@/lib/format'
import { cn, displayName, Permissions } from '@/lib/utils'
import { useDentists } from '@/lib/queries'
import type { Appointment, AppointmentStatus, FrontDesk, FrontDeskAppointment } from '@/lib/types'
import { useAuth } from '@/auth/AuthProvider'
import { Button } from '@/components/ui/Button'
import { Badge, Card, CardHeader, EmptyState, ErrorState, LoadingBlock, PageHeader, Select, StatCard } from '@/components/ui/primitives'
import { AppointmentStatusBadge } from '@/components/StatusBadge'
import { PatientPicker, type PickedPatient } from '@/components/PatientPicker'
import { AppointmentDialog, type AppointmentDraft } from '@/features/appointments/AppointmentDialog'
import { RecordPaymentDialog } from '@/features/billing/RecordPaymentDialog'
import { PatientFormDialog } from '@/features/patients/PatientFormDialog'
import { SendMessageDialog } from '@/features/messages/SendMessageDialog'

/** The one step a receptionist most likely takes next for an appointment in each state. */
const nextStep: Partial<Record<AppointmentStatus, AppointmentStatus>> = {
  Scheduled: 'Arrived', Confirmed: 'Arrived', Arrived: 'InTreatment', InTreatment: 'Completed',
}

/** Reception's working screen: today's list with one-click check-in, and find, register, book and take payment without leaving it. */
export default function FrontDeskPage() {
  const { t } = useTranslation()
  const { can } = useAuth()
  const qc = useQueryClient()
  const navigate = useNavigate()
  const dentists = useDentists()
  const [dentistId, setDentistId] = useState('')
  const [found, setFound] = useState<PickedPatient | null>(null)
  const [booking, setBooking] = useState<AppointmentDraft | null>(null)
  const [paying, setPaying] = useState<PickedPatient | null>(null)
  const [messaging, setMessaging] = useState<PickedPatient | null>(null)
  const [registering, setRegistering] = useState(false)
  const searchBox = useRef<HTMLDivElement>(null)
  const canManage = can(Permissions.AppointmentsManage)

  const day = useQuery({
    queryKey: ['front-desk', dentistId],
    queryFn: () => api.get<FrontDesk>('/front-desk', { dentistId }),
    refetchInterval: 60_000,
  })
  const refresh = () => {
    void qc.invalidateQueries({ queryKey: ['front-desk'] })
    void qc.invalidateQueries({ queryKey: ['appointments'] })
    void qc.invalidateQueries({ queryKey: ['dashboard'] })
  }

  const setStatus = useMutation({
    mutationFn: (v: { id: string; status: AppointmentStatus }) => api.patch<Appointment>(`/appointments/${v.id}/status`, { status: v.status }),
    onSuccess: refresh,
    onError: (e) => toast.error(errorMessage(e)),
  })

  // Keyboard: "/" finds a patient, "n" registers one, "b" books.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement
      if (e.ctrlKey || e.metaKey || e.altKey || target.closest('input, textarea, select, [contenteditable], [role=dialog]')) return
      if (e.key === '/') { e.preventDefault(); searchBox.current?.querySelector('input')?.focus() }
      else if (e.key === 'n' && can(Permissions.PatientsCreate)) setRegistering(true)
      else if (e.key === 'b' && canManage) setBooking({})
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [can, canManage])

  if (day.error) return <ErrorState error={day.error} onRetry={() => void day.refetch()} />
  const d = day.data

  return (
    <div className="space-y-6">
      <PageHeader icon={<ConciergeBell className="size-6 text-primary" />} title={t('frontDesk.title')} subtitle={day.data ? weekday(`${day.data.date}T12:00:00Z`) : undefined}
        actions={
          <>
            {can(Permissions.PatientsCreate) && <Button variant="outline" icon={<UserPlus className="size-4" />} onClick={() => setRegistering(true)}>{t('frontDesk.newPatient')} <kbd className="ms-1 hidden rounded border border-line px-1 text-[10px] sm:inline">N</kbd></Button>}
            {canManage && <Button icon={<CalendarPlus className="size-4" />} onClick={() => setBooking(found ? { patient: found } : {})}>{t('frontDesk.book')} <kbd className="ms-1 hidden rounded border border-white/40 px-1 text-[10px] sm:inline">B</kbd></Button>}
          </>
        } />

      {d?.closed && (
        <p role="status" className="flex items-center gap-2 rounded-xl border border-warning/30 bg-warning-soft px-4 py-3 text-sm text-warning-ink">
          <AlertTriangle className="size-4 shrink-0" />{t(`frontDesk.closed.${d.closed === 'holiday' ? 'holiday' : 'dayOff'}`)}
        </p>
      )}

      <div className="grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-6">
        <StatCard label={t('frontDesk.expected')} value={d?.counts.expected ?? '—'} icon={<Clock className="size-4" />} />
        <StatCard label={t('frontDesk.waiting')} value={d?.counts.arrived ?? '—'} tone="warning" />
        <StatCard label={t('frontDesk.inTreatment')} value={d?.counts.inTreatment ?? '—'} tone="info" />
        <StatCard label={t('frontDesk.done')} value={d?.counts.completed ?? '—'} tone="good" hint={d && d.counts.noShow > 0 ? t('frontDesk.noShows', { count: d.counts.noShow }) : undefined} />
        <StatCard label={t('frontDesk.tomorrowUnconfirmed')} value={d?.tomorrowUnconfirmed ?? '—'} onClick={() => navigate('/appointments')} />
        {d?.collectedToday != null
          ? <StatCard label={t('frontDesk.collected')} value={money(d.collectedToday)} tone="good" icon={<CircleDollarSign className="size-4" />} onClick={() => navigate('/payments')} />
          : <StatCard label={t('frontDesk.followUps')} value={d?.followUpsDue ?? '—'} tone="recovery" onClick={() => navigate('/recovery')} />}
      </div>

      <div className="grid gap-6 xl:grid-cols-[1fr_340px]">
        <Card>
          <CardHeader title={t('frontDesk.today')} actions={
            <Select aria-label={t('frontDesk.allDentists')} value={dentistId} onChange={(e) => setDentistId(e.target.value)} className="w-48">
              <option value="">{t('frontDesk.allDentists')}</option>
              {(dentists.data ?? []).map((x) => <option key={x.id} value={x.id}>{x.fullName}</option>)}
            </Select>
          } />
          {!d ? <LoadingBlock rows={6} /> : d.appointments.length === 0 ? (
            <EmptyState icon={<Clock className="size-6" />} title={t('frontDesk.empty')}
              action={canManage ? <Button icon={<CalendarPlus className="size-4" />} onClick={() => setBooking({})}>{t('frontDesk.book')}</Button> : undefined} />
          ) : (
            <ul className="divide-y divide-line">
              {d.appointments.map((row) => (
                <Row key={row.appointment.id} row={row} canManage={canManage} canPay={can(Permissions.PaymentsManage)} busy={setStatus.isPending && setStatus.variables?.id === row.appointment.id}
                  onStatus={(status) => setStatus.mutate({ id: row.appointment.id, status })}
                  onPay={() => setPaying({ id: row.appointment.patientId, fullName: row.appointment.patientName, fullNameAr: row.appointment.patientNameAr, phone: row.appointment.patientPhone })}
                  onMessage={() => setMessaging({ id: row.appointment.patientId, fullName: row.appointment.patientName, fullNameAr: row.appointment.patientNameAr, phone: row.appointment.patientPhone })} />
              ))}
            </ul>
          )}
        </Card>

        <div className="space-y-6">
          <Card className="p-5">
            <h2 className="text-sm font-semibold text-ink">{t('frontDesk.find')} <kbd className="ms-1 rounded border border-line px-1 text-[10px] font-normal text-ink-3">/</kbd></h2>
            <div ref={searchBox} className="mt-3"><PatientPicker value={found} onChange={setFound} /></div>
            {found && (
              <div className="mt-3 flex flex-wrap gap-2">
                <Link to={`/patients/${found.id}`}><Button size="sm" variant="outline">{t('frontDesk.openFile')}</Button></Link>
                {canManage && <Button size="sm" variant="outline" icon={<CalendarPlus className="size-3.5" />} onClick={() => setBooking({ patient: found })}>{t('frontDesk.book')}</Button>}
                {can(Permissions.PaymentsManage) && <Button size="sm" variant="outline" icon={<CircleDollarSign className="size-3.5" />} onClick={() => setPaying(found)}>{t('frontDesk.takePayment')}</Button>}
                {can(Permissions.MessagesSend) && <Button size="sm" variant="outline" icon={<MessageCircle className="size-3.5" />} onClick={() => setMessaging(found)}>{t('frontDesk.message')}</Button>}
              </div>
            )}
          </Card>

          {can(Permissions.RecoveryView) && d && d.followUpsDue > 0 && (
            <Link to="/recovery" className="block rounded-2xl border border-recovery/30 bg-recovery-soft p-5 transition-colors hover:border-recovery">
              <p className="flex items-center gap-2 text-sm font-semibold text-recovery-ink"><Sparkles className="size-4" />{t('frontDesk.followUpsDue', { count: d.followUpsDue })}</p>
              <p className="mt-1 text-sm text-ink-2">{t('frontDesk.followUpsBody')}</p>
            </Link>
          )}
        </div>
      </div>

      {booking && <AppointmentDialog open onClose={() => setBooking(null)} draft={booking} onSaved={refresh} />}
      <RecordPaymentDialog open={!!paying} onClose={() => { setPaying(null); refresh() }} patient={paying} />
      <SendMessageDialog open={!!messaging} onClose={() => setMessaging(null)} patient={messaging} />
      <PatientFormDialog open={registering} onClose={() => setRegistering(false)} onSaved={(id) => navigate(`/patients/${id}`)} />
    </div>
  )
}

function Row({ row, canManage, canPay, busy, onStatus, onPay, onMessage }: {
  row: FrontDeskAppointment; canManage: boolean; canPay: boolean; busy: boolean
  onStatus: (s: AppointmentStatus) => void; onPay: () => void; onMessage: () => void
}) {
  const { t } = useTranslation()
  const a = row.appointment
  const next = nextStep[a.status]
  const finished = a.status === 'Completed' || a.status === 'Cancelled' || a.status === 'NoShow'
  return (
    <li className={cn('flex flex-wrap items-center gap-x-4 gap-y-2 px-5 py-3', finished && 'opacity-60')}>
      <div className="w-16 shrink-0">
        <p className="tabular text-sm font-semibold text-ink">{time(a.startTime)}</p>
        <p className="text-[11px] text-ink-3">{a.durationMinutes}′</p>
      </div>
      <span className="h-9 w-1 shrink-0 rounded-full" style={{ background: a.dentistColor }} aria-hidden />
      <div className="min-w-0 flex-1">
        <p className="flex flex-wrap items-center gap-2">
          <Link to={`/patients/${a.patientId}`} className="truncate text-sm font-medium text-ink hover:underline">{displayName(a.patientName, a.patientNameAr)}</Link>
          {a.hasMedicalAlert && <span title={t('frontDesk.medicalAlert')} className="text-critical"><HeartPulse className="size-4" aria-label={t('frontDesk.medicalAlert')} /></span>}
          {row.isLate && <Badge tone="warning">{t('frontDesk.late')}</Badge>}
          {row.patientBalance != null && row.patientBalance > 0 && <Badge tone="critical">{t('frontDesk.owes', { amount: money(row.patientBalance) })}</Badge>}
        </p>
        <p className="mt-0.5 truncate text-xs text-ink-3">{a.treatment} · {a.dentistName} · <span className="ltr">{a.patientPhone}</span></p>
      </div>
      <AppointmentStatusBadge status={a.status} />
      <div className="flex shrink-0 flex-wrap justify-end gap-1.5">
        {canManage && a.status === 'Scheduled' && <Button size="sm" variant="ghost" loading={busy} onClick={() => onStatus('Confirmed')}>{t('frontDesk.confirm')}</Button>}
        {canManage && next && <Button size="sm" loading={busy} onClick={() => onStatus(next)}>{t(`frontDesk.step.${next}`)}</Button>}
        {canManage && row.isLate && <Button size="sm" variant="outline" loading={busy} onClick={() => onStatus('NoShow')}>{t('frontDesk.noShow')}</Button>}
        {canPay && row.patientBalance != null && row.patientBalance > 0 && <Button size="sm" variant="outline" icon={<CircleDollarSign className="size-3.5" />} onClick={onPay}>{t('frontDesk.takePayment')}</Button>}
        {!finished && <Button size="icon" variant="ghost" aria-label={t('frontDesk.message')} onClick={onMessage}><MessageCircle className="size-4" /></Button>}
      </div>
    </li>
  )
}
