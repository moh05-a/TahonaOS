import { NavLink } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import {
  BarChart3, Bot, CalendarDays, ClipboardList, ConciergeBell, CreditCard, FileText, LayoutDashboard, ListChecks, MessageCircle, Settings, Smile, Sparkles,
  Upload, Users, UserCog,
} from 'lucide-react'
import { TahonaLogo } from '@/components/brand/TahonaLogo'
import { useAuth } from '@/auth/AuthProvider'
import { api } from '@/lib/api'
import { cn, Permissions, type Permission } from '@/lib/utils'
import type { RecoveryDashboard } from '@/lib/types'

interface NavItem { to: string; label: string; icon: React.ElementType; permission: Permission; recovery?: boolean }

export function Sidebar({ onNavigate }: { onNavigate?: () => void }) {
  const { t, i18n } = useTranslation()
  const { can, user } = useAuth()

  const toContact = useQuery({
    queryKey: ['recovery', 'badge'],
    queryFn: () => api.get<RecoveryDashboard>('/recovery/dashboard', { take: 1 }),
    enabled: can(Permissions.RecoveryView),
    staleTime: 60_000,
    select: (d) => d.summary.patientsToContact,
  })

  const groups: { label?: string; items: NavItem[] }[] = [
    {
      items: [
        { to: '/', label: t('nav.dashboard'), icon: LayoutDashboard, permission: Permissions.DashboardView },
        { to: '/front-desk', label: t('nav.frontDesk'), icon: ConciergeBell, permission: Permissions.AppointmentsView },
        { to: '/recovery', label: t('nav.recovery'), icon: Sparkles, permission: Permissions.RecoveryView, recovery: true },
        { to: '/appointments', label: t('nav.appointments'), icon: CalendarDays, permission: Permissions.AppointmentsView },
        { to: '/patients', label: t('nav.patients'), icon: Users, permission: Permissions.PatientsView },
      ],
    },
    {
      label: t('nav.clinical'),
      items: [
        { to: '/treatment-plans', label: t('nav.treatmentPlans'), icon: ClipboardList, permission: Permissions.TreatmentView },
        { to: '/dental-chart', label: t('nav.dentalChart'), icon: Smile, permission: Permissions.ChartView },
      ],
    },
    {
      label: t('nav.finance'),
      items: [
        { to: '/invoices', label: t('nav.invoices'), icon: FileText, permission: Permissions.BillingView },
        { to: '/payments', label: t('nav.payments'), icon: CreditCard, permission: Permissions.BillingView },
        { to: '/reports', label: t('nav.reports'), icon: BarChart3, permission: Permissions.ReportsView },
      ],
    },
    {
      label: t('nav.growth'),
      items: [
        { to: '/messages', label: t('nav.messages'), icon: MessageCircle, permission: Permissions.MessagesView },
        { to: '/assistant', label: t('nav.assistant'), icon: Bot, permission: Permissions.AiUse },
      ],
    },
    {
      label: t('nav.admin'),
      items: [
        { to: '/onboarding', label: t('nav.onboarding'), icon: ListChecks, permission: Permissions.SettingsManage },
        { to: '/import', label: t('nav.import'), icon: Upload, permission: Permissions.DataImport },
        { to: '/staff', label: t('nav.staff'), icon: UserCog, permission: Permissions.StaffManage },
        { to: '/settings', label: t('nav.settings'), icon: Settings, permission: Permissions.SettingsManage },
      ],
    },
  ]

  return (
    <aside className="flex h-full w-64 flex-col border-e border-line bg-surface">
      <div className="flex h-16 items-center px-5">
        <TahonaLogo size={30} lang={i18n.language === 'ar' ? 'ar' : 'en'} subtitle={user?.clinic.name} />
      </div>

      <nav className="flex-1 space-y-5 overflow-y-auto px-3 py-3">
        {groups.map((group, gi) => {
          const items = group.items.filter((i) => can(i.permission))
          if (items.length === 0) return null
          return (
            <div key={gi}>
              {group.label && <p className="mb-1.5 px-3 text-[10px] font-semibold tracking-wider text-ink-3 uppercase">{group.label}</p>}
              <ul className="space-y-0.5">
                {items.map((item) => (
                  <li key={item.to}>
                    <NavLink
                      to={item.to}
                      end={item.to === '/'}
                      onClick={onNavigate}
                      className={({ isActive }) =>
                        cn(
                          'group flex items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium transition-colors',
                          item.recovery
                            ? isActive
                              ? 'bg-recovery-gradient text-white shadow-sm'
                              : 'bg-recovery-soft text-recovery-ink hover:brightness-95'
                            : isActive
                              ? 'bg-primary-soft text-primary-ink'
                              : 'text-ink-2 hover:bg-surface-3 hover:text-ink',
                        )
                      }
                    >
                      {({ isActive }) => (
                        <>
                          <item.icon className="size-4 shrink-0" />
                          <span className="flex-1 truncate">{item.label}</span>
                          {item.recovery && !!toContact.data && (
                            <span className={cn('rounded-full px-1.5 py-0.5 text-[10px] font-semibold tabular', isActive ? 'bg-white/25 text-white' : 'bg-recovery text-white')}>
                              {toContact.data}
                            </span>
                          )}
                        </>
                      )}
                    </NavLink>
                  </li>
                ))}
              </ul>
            </div>
          )
        })}
      </nav>

      <div className="m-3 rounded-xl border border-line bg-surface-2 p-3 text-xs text-ink-3">
        <p className="font-medium text-ink-2">{user?.clinic.name}</p>
        <p>{user?.clinic.currency} · {user?.clinic.timeZone}</p>
      </div>
    </aside>
  )
}
