import { useMemo, useRef, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient, keepPreviousData } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import FullCalendar from '@fullcalendar/react'
import dayGridPlugin from '@fullcalendar/daygrid'
import timeGridPlugin from '@fullcalendar/timegrid'
import interactionPlugin from '@fullcalendar/interaction'
import luxonPlugin from '@fullcalendar/luxon3'
import arLocale from '@fullcalendar/core/locales/ar'
import type { DateSelectArg, DatesSetArg, EventClickArg, EventDropArg, EventInput } from '@fullcalendar/core'
import type { EventResizeDoneArg } from '@fullcalendar/interaction'
import { AlertTriangle, CalendarPlus, Check, CheckCircle2, MessageCircle, Pencil, Play, Sparkles, UserCheck, UserRound, X, XCircle } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { clinicTimeZone, dateTime, time } from '@/lib/format'
import { useDentists } from '@/lib/queries'
import { cn, displayName, Permissions } from '@/lib/utils'
import type { Appointment, AppointmentStatus, ClinicSettings, SendResult } from '@/lib/types'
import { useAuth } from '@/auth/AuthProvider'
import { Badge, Card, ErrorState, PageHeader, Segmented, Select, Textarea } from '@/components/ui/primitives'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { AppointmentStatusBadge } from '@/components/StatusBadge'
import { AppointmentDialog, type AppointmentDraft } from './AppointmentDialog'

type CalView = 'timeGridDay' | 'timeGridWeek' | 'dayGridMonth'

const toolbar = { start: 'prev,next today', center: 'title', end: '' }

export default function AppointmentsPage() {
  const { t, i18n } = useTranslation()
  const { can } = useAuth()
  const canManage = can(Permissions.AppointmentsManage)
  const qc = useQueryClient()
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const calRef = useRef<FullCalendar>(null)
  const [view, setView] = useState<CalView>('timeGridWeek')
  const [range, setRange] = useState<{ start: string; end: string } | null>(null)
  const [dentistId, setDentistId] = useState('')
  const [draft, setDraft] = useState<AppointmentDraft | null>(null)
  const [selected, setSelected] = useState<Appointment | null>(null)
  const dentists = useDentists()
  const settings = useQuery({ queryKey: ['settings'], queryFn: () => api.get<ClinicSettings>('/settings'), staleTime: 300_000 })

  const appts = useQuery({
    queryKey: ['appointments', range, dentistId],
    queryFn: () => api.get<Appointment[]>('/appointments', { from: range!.start, to: range!.end, dentistId }),
    enabled: !!range,
    placeholderData: keepPreviousData,
  })

  const events: EventInput[] = useMemo(
    () => (appts.data ?? []).map((a) => ({
      id: a.id,
      title: displayName(a.patientName, a.patientNameAr),
      start: a.startTime,
      end: a.endTime,
      backgroundColor: a.status === 'Cancelled' ? 'transparent' : `color-mix(in srgb, ${a.dentistColor} ${a.status === 'Completed' ? 10 : 16}%, var(--surface))`,
      borderColor: a.dentistColor,
      textColor: 'var(--text)',
      editable: canManage && !['Completed', 'Cancelled', 'InTreatment'].includes(a.status),
      classNames: [`appt-${a.status}`],
      extendedProps: a,
    })),
    [appts.data, canManage],
  )

  const reschedule = useMutation({
    mutationFn: (v: { id: string; start: string; end: string; dentistId?: string }) =>
      api.patch<Appointment>(`/appointments/${v.id}/reschedule`, { startTime: v.start, endTime: v.end, dentistId: v.dentistId }),
    onSuccess: () => {
      toast.success(t('appointments.rescheduled'))
      void qc.invalidateQueries({ queryKey: ['appointments'] })
      void qc.invalidateQueries({ queryKey: ['dashboard'] })
    },
    onError: (e) => {
      toast.error(errorMessage(e))
      void qc.invalidateQueries({ queryKey: ['appointments'] })
    },
  })

  const onMove = (arg: EventDropArg | EventResizeDoneArg) => {
    if (!arg.event.start || !arg.event.end) return arg.revert()
    reschedule.mutate({ id: arg.event.id, start: arg.event.start.toISOString(), end: arg.event.end.toISOString() }, { onError: () => arg.revert() })
  }

  const initialDate = params.get('date') ?? undefined
  const sh = settings.data?.workingHoursStart?.slice(0, 5) ?? '08:00'
  const eh = settings.data?.workingHoursEnd?.slice(0, 5) ?? '21:00'
  const workingDays = settings.data?.workingDays.join(',')
  const hiddenDays = useMemo(() => (workingDays ? [0, 1, 2, 3, 4, 5, 6].filter((d) => !workingDays.split(',').map(Number).includes(d)) : [5]), [workingDays])

  const changeView = (v: CalView) => {
    setView(v)
    calRef.current?.getApi().changeView(v)
  }

  return (
    <div className="space-y-5">
      <PageHeader
        title={t('appointments.title')}
        actions={
          <>
            <Select value={dentistId} onChange={(e) => setDentistId(e.target.value)} className="w-48">
              <option value="">{t('appointments.allDentists')}</option>
              {dentists.data?.map((d) => <option key={d.id} value={d.id}>{d.fullName}</option>)}
            </Select>
            <Segmented<CalView>
              value={view}
              onChange={changeView}
              items={[
                { value: 'timeGridDay', label: t('appointments.day') },
                { value: 'timeGridWeek', label: t('appointments.week') },
                { value: 'dayGridMonth', label: t('appointments.month') },
              ]}
            />
            {canManage && <Button icon={<CalendarPlus className="size-4" />} onClick={() => setDraft({})}>{t('appointments.new')}</Button>}
          </>
        }
      />

      <div className="flex flex-wrap gap-3 text-xs text-ink-2">
        {dentists.data?.map((d) => (
          <button key={d.id} onClick={() => setDentistId(dentistId === d.id ? '' : d.id)}
            className={cn('inline-flex items-center gap-1.5 rounded-full border px-2.5 py-1', dentistId === d.id ? 'border-line-strong bg-surface' : 'border-transparent')}>
            <span className="size-2.5 rounded-full" style={{ background: d.color }} />{d.fullName}
          </button>
        ))}
      </div>

      {appts.error && <ErrorState error={appts.error} onRetry={() => void appts.refetch()} />}

      <Card className="p-3 sm:p-4">
        <FullCalendar
          ref={calRef}
          plugins={[dayGridPlugin, timeGridPlugin, interactionPlugin, luxonPlugin]}
          timeZone={clinicTimeZone()}
          locale={i18n.language === 'ar' ? arLocale : 'en-gb'}
          direction={i18n.language === 'ar' ? 'rtl' : 'ltr'}
          initialView={view}
          initialDate={initialDate}
          headerToolbar={toolbar}
          height="auto"
          firstDay={6}
          hiddenDays={hiddenDays}
          slotMinTime={sh}
          slotMaxTime={eh}
          slotDuration={`00:${String(settings.data?.slotMinutes ?? 30).padStart(2, '0')}:00`}
          allDaySlot={false}
          nowIndicator
          dayMaxEvents={4}
          selectable={canManage}
          selectMirror
          editable={canManage}
          eventDurationEditable={canManage}
          events={events}
          datesSet={(arg: DatesSetArg) => {
            const next = { start: arg.start.toISOString(), end: arg.end.toISOString() }
            setRange((r) => (r?.start === next.start && r.end === next.end ? r : next))
          }}
          select={(arg: DateSelectArg) => {
            const mins = Math.round((arg.end.getTime() - arg.start.getTime()) / 60_000)
            setDraft({ start: arg.start.toISOString(), durationMinutes: arg.view.type === 'dayGridMonth' ? 30 : mins, dentistId: dentistId || undefined })
            calRef.current?.getApi().unselect()
          }}
          eventClick={(arg: EventClickArg) => setSelected(arg.event.extendedProps as Appointment)}
          eventDrop={onMove}
          eventResize={onMove}
          eventContent={(arg) => {
            const a = arg.event.extendedProps as Appointment
            return (
              <div className={cn('flex h-full flex-col overflow-hidden leading-tight', a.status === 'Cancelled' && 'line-through opacity-50')}>
                <div className="flex items-center gap-1 text-[11px] font-semibold">
                  {a.hasMedicalAlert && <AlertTriangle className="size-3 shrink-0 text-critical" />}
                  {a.fromRecovery && <Sparkles className="size-3 shrink-0 text-recovery" />}
                  {a.status === 'NoShow' && <XCircle className="size-3 shrink-0 text-critical" />}
                  {a.status === 'Completed' && <CheckCircle2 className="size-3 shrink-0 text-good" />}
                  <span className="truncate">{arg.event.title}</span>
                </div>
                <div className="truncate text-[10px] text-ink-2">{arg.timeText} · {a.treatment}</div>
              </div>
            )
          }}
        />
      </Card>

      <AppointmentDetail
        appointment={selected}
        onClose={() => setSelected(null)}
        canManage={canManage}
        onEdit={(a) => {
          setSelected(null)
          setDraft({
            id: a.id, patient: { id: a.patientId, fullName: a.patientName, fullNameAr: a.patientNameAr, phone: a.patientPhone }, dentistId: a.dentistId,
            start: a.startTime, durationMinutes: a.durationMinutes, treatment: a.treatment, notes: a.notes, chair: a.chair, treatmentPlanId: a.treatmentPlanId,
          })
        }}
        onOpenPatient={(a) => navigate(`/patients/${a.patientId}`)}
      />
      <AppointmentDialog open={!!draft} draft={draft ?? {}} onClose={() => setDraft(null)} />
    </div>
  )
}

const transitions: Partial<Record<AppointmentStatus, { to: AppointmentStatus; key: string; icon: React.ElementType; variant?: 'primary' | 'outline' | 'danger' }[]>> = {
  Scheduled: [{ to: 'Confirmed', key: 'appointments.confirm', icon: Check }, { to: 'Arrived', key: 'appointments.checkIn', icon: UserCheck, variant: 'primary' }],
  Confirmed: [{ to: 'Arrived', key: 'appointments.checkIn', icon: UserCheck, variant: 'primary' }],
  Arrived: [{ to: 'InTreatment', key: 'appointments.start_treatment', icon: Play, variant: 'primary' }],
  InTreatment: [{ to: 'Completed', key: 'appointments.complete', icon: CheckCircle2, variant: 'primary' }],
}

function AppointmentDetail({ appointment: a, onClose, canManage, onEdit, onOpenPatient }: {
  appointment: Appointment | null; onClose: () => void; canManage: boolean; onEdit: (a: Appointment) => void; onOpenPatient: (a: Appointment) => void
}) {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const [cancelling, setCancelling] = useState(false)
  const [reason, setReason] = useState('')

  const setStatus = useMutation({
    mutationFn: (v: { status: AppointmentStatus; reason?: string }) => api.patch<Appointment>(`/appointments/${a!.id}/status`, v),
    onSuccess: () => {
      toast.success(t('common.updated'))
      void qc.invalidateQueries({ queryKey: ['appointments'] })
      void qc.invalidateQueries({ queryKey: ['dashboard'] })
      void qc.invalidateQueries({ queryKey: ['recovery'] })
      setCancelling(false)
      onClose()
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  const remind = useMutation({
    mutationFn: () => api.post<SendResult>('/messages/send', { patientId: a!.patientId, channel: 'WhatsApp', templateType: 'AppointmentReminder', appointmentId: a!.id }),
    onSuccess: (r) => {
      if (r.manualSendUrl) window.open(r.manualSendUrl, '_blank', 'noopener')
      toast.success(t('appointments.reminderSent'))
      void qc.invalidateQueries({ queryKey: ['appointments'] })
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  if (!a) return null
  const past = new Date(a.startTime) < new Date()
  const open = !['Completed', 'Cancelled', 'NoShow'].includes(a.status)

  return (
    <Dialog
      open={!!a}
      onClose={onClose}
      title={displayName(a.patientName, a.patientNameAr)}
      description={`${dateTime(a.startTime)} – ${time(a.endTime)} · ${a.dentistName}`}
      footer={
        <>
          <Button variant="ghost" icon={<UserRound className="size-4" />} onClick={() => onOpenPatient(a)} className="me-auto">{t('recovery.viewPatient')}</Button>
          {canManage && open && (
            <>
              <Button variant="outline" icon={<Pencil className="size-4" />} onClick={() => onEdit(a)}>{t('common.edit')}</Button>
              {transitions[a.status]?.map((tr) => (
                <Button key={tr.to} variant={tr.variant ?? 'outline'} icon={<tr.icon className="size-4" />} loading={setStatus.isPending && setStatus.variables?.status === tr.to}
                  onClick={() => setStatus.mutate({ status: tr.to })}>{t(tr.key as 'appointments.confirm')}</Button>
              ))}
            </>
          )}
        </>
      }
    >
      <div className="space-y-4">
        <div className="flex flex-wrap items-center gap-2">
          <AppointmentStatusBadge status={a.status} />
          {a.fromRecovery && <Badge tone="recovery"><Sparkles className="size-3" />{t('appointments.fromRecovery')}</Badge>}
          {a.hasMedicalAlert && <Badge tone="critical"><AlertTriangle className="size-3" />{t('patients.medicalAlert')}</Badge>}
          {a.reminderSentAt && <Badge tone="good"><MessageCircle className="size-3" />{t('appointments.reminderSent')}</Badge>}
        </div>
        <dl className="grid grid-cols-2 gap-3 text-sm">
          <div><dt className="text-xs text-ink-3">{t('common.treatment')}</dt><dd className="font-medium text-ink">{a.treatment}</dd></div>
          <div><dt className="text-xs text-ink-3">{t('appointments.duration')}</dt><dd className="font-medium text-ink">{a.durationMinutes}</dd></div>
          <div><dt className="text-xs text-ink-3">{t('common.phone')}</dt><dd className="ltr font-medium text-ink">{a.patientPhone}</dd></div>
          <div><dt className="text-xs text-ink-3">{t('appointments.chair')}</dt><dd className="font-medium text-ink">{a.chair ?? '—'}</dd></div>
        </dl>
        {a.notes && <p className="rounded-lg bg-surface-2 p-3 text-sm text-ink-2">{a.notes}</p>}

        {canManage && open && (
          <div className="flex flex-wrap gap-2 border-t border-line pt-4">
            {!past && can(a.status, 'Scheduled', 'Confirmed') && (
              <Button size="sm" variant="outline" icon={<MessageCircle className="size-3.5" />} loading={remind.isPending} onClick={() => remind.mutate()}>
                {t('appointments.sendReminder')}
              </Button>
            )}
            {past && can(a.status, 'Scheduled', 'Confirmed') && (
              <Button size="sm" variant="outline" icon={<XCircle className="size-3.5" />} onClick={() => setStatus.mutate({ status: 'NoShow' })}>{t('appointments.noShow')}</Button>
            )}
            <Button size="sm" variant="ghost" icon={<X className="size-3.5" />} onClick={() => setCancelling((c) => !c)}>{t('appointments.cancelAppt')}</Button>
          </div>
        )}
        {cancelling && (
          <div className="space-y-2">
            <Textarea placeholder={t('appointments.cancelReason')} value={reason} onChange={(e) => setReason(e.target.value)} rows={2} maxLength={500} />
            <Button size="sm" variant="danger" loading={setStatus.isPending} onClick={() => setStatus.mutate({ status: 'Cancelled', reason })}>{t('appointments.cancelAppt')}</Button>
          </div>
        )}
      </div>
    </Dialog>
  )
}

const can = (status: AppointmentStatus, ...allowed: AppointmentStatus[]) => allowed.includes(status)
