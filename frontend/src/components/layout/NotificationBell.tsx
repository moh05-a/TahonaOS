import { useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { AlertTriangle, Bell, CalendarClock, CalendarX, CheckCheck, CircleDollarSign, Sparkles, UserPlus } from 'lucide-react'
import { api } from '@/lib/api'
import { relative } from '@/lib/format'
import { cn } from '@/lib/utils'
import type { AppNotification, NotificationType, PriorityLevel } from '@/lib/types'
import { Menu } from '@/components/ui/Menu'
import { Button } from '@/components/ui/Button'
import { EmptyState } from '@/components/ui/primitives'

const typeIcon: Partial<Record<NotificationType, React.ElementType>> = {
  NewAppointment: CalendarClock, UpcomingAppointment: CalendarClock, MissedAppointment: CalendarX, OverduePayment: CircleDollarSign,
  TreatmentFollowUp: Sparkles, HighValueInactiveTreatment: Sparkles, NewPatient: UserPlus,
}
const priorityClass: Record<PriorityLevel, string> = {
  Urgent: 'bg-critical-soft text-critical-ink', High: 'bg-serious-soft text-serious-ink', Medium: 'bg-warning-soft text-warning-ink', Low: 'bg-surface-3 text-ink-2',
}

export function NotificationBell() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const qc = useQueryClient()
  const { data } = useQuery({
    queryKey: ['notifications'],
    queryFn: () => api.get<{ items: AppNotification[]; unreadCount: number }>('/notifications', { take: 30 }),
    refetchInterval: 60_000,
  })
  const markRead = useMutation({
    mutationFn: (id: string) => api.post(`/notifications/${id}/read`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['notifications'] }),
  })
  const markAll = useMutation({
    mutationFn: () => api.post('/notifications/read-all'),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['notifications'] }),
  })
  const unread = data?.unreadCount ?? 0

  return (
    <Menu
      className="w-[min(92vw,380px)] p-0"
      trigger={({ toggle }) => (
        <Button variant="ghost" size="icon" onClick={toggle} aria-label={t('notifications.title')} className="relative">
          <Bell className="size-4" />
          {unread > 0 && (
            <span className="absolute -top-0.5 -end-0.5 grid min-w-4 place-items-center rounded-full bg-critical px-1 text-[10px] font-semibold text-white">
              {unread > 99 ? '99+' : unread}
            </span>
          )}
        </Button>
      )}
    >
      {(close) => (
        <div>
          <div className="flex items-center justify-between border-b border-line px-4 py-3">
            <p className="text-sm font-semibold text-ink">{t('notifications.title')}</p>
            {unread > 0 && (
              <button className="flex items-center gap-1 text-xs text-primary-ink hover:underline" onClick={() => markAll.mutate()}>
                <CheckCheck className="size-3.5" /> {t('notifications.markAll')}
              </button>
            )}
          </div>
          <div className="max-h-[60vh] overflow-y-auto">
            {!data?.items.length ? (
              <EmptyState icon={<Bell className="size-5" />} title={t('notifications.empty')} className="py-8" />
            ) : (
              data.items.map((n) => {
                const Icon = typeIcon[n.type] ?? AlertTriangle
                return (
                  <button
                    key={n.id}
                    onClick={() => {
                      if (!n.isRead) markRead.mutate(n.id)
                      close()
                      if (n.link) navigate(n.link)
                    }}
                    className={cn('flex w-full gap-3 border-b border-line px-4 py-3 text-start last:border-0 hover:bg-surface-2', !n.isRead && 'bg-primary-soft/40')}
                  >
                    <span className={cn('grid size-8 shrink-0 place-items-center rounded-lg', priorityClass[n.priority])}>
                      <Icon className="size-4" />
                    </span>
                    <span className="min-w-0 flex-1">
                      <span className="flex items-center gap-2">
                        <span className="truncate text-sm font-medium text-ink">{n.title}</span>
                        {!n.isRead && <span className="size-1.5 shrink-0 rounded-full bg-primary" />}
                      </span>
                      {n.body && <span className="line-clamp-2 text-xs text-ink-3">{n.body}</span>}
                      <span className="mt-0.5 block text-[11px] text-ink-3">
                        {t(`enum.PriorityLevel.${n.priority}`)} · {relative(n.createdAt)}
                      </span>
                    </span>
                  </button>
                )
              })
            )}
          </div>
        </div>
      )}
    </Menu>
  )
}
