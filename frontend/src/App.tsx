import { lazy, Suspense, type ReactNode } from 'react'
import { BrowserRouter, Navigate, Route, Routes, useLocation } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { ShieldAlert } from 'lucide-react'
import { useAuth } from '@/auth/AuthProvider'
import { AppShell } from '@/components/layout/AppShell'
import { EmptyState, Spinner } from '@/components/ui/primitives'
import { Permissions, type Permission } from '@/lib/utils'
import LoginPage from '@/features/auth/LoginPage'

const DashboardPage = lazy(() => import('@/features/dashboard/DashboardPage'))
const RecoveryPage = lazy(() => import('@/features/recovery/RecoveryPage'))
const AppointmentsPage = lazy(() => import('@/features/appointments/AppointmentsPage'))
const PatientsPage = lazy(() => import('@/features/patients/PatientsPage'))
const PatientProfilePage = lazy(() => import('@/features/patients/PatientProfilePage'))
const TreatmentPlansPage = lazy(() => import('@/features/plans/TreatmentPlansPage'))
const PlanDetailPage = lazy(() => import('@/features/plans/PlanDetailPage'))
const DentalChartPage = lazy(() => import('@/features/chart/DentalChartPage'))
const InvoicesPage = lazy(() => import('@/features/billing/InvoicesPage'))
const InvoiceDetailPage = lazy(() => import('@/features/billing/InvoiceDetailPage'))
const PaymentsPage = lazy(() => import('@/features/billing/PaymentsPage'))
const ReportsPage = lazy(() => import('@/features/reports/ReportsPage'))
const MessagesPage = lazy(() => import('@/features/messages/MessagesPage'))
const AssistantPage = lazy(() => import('@/features/assistant/AssistantPage'))
const StaffPage = lazy(() => import('@/features/staff/StaffPage'))
const SettingsPage = lazy(() => import('@/features/settings/SettingsPage'))

function FullPageSpinner() {
  return (
    <div className="grid h-full min-h-60 place-items-center">
      <Spinner className="size-7" />
    </div>
  )
}

function RequireAuth({ children }: { children: ReactNode }) {
  const { status } = useAuth()
  const location = useLocation()
  if (status === 'loading') return <FullPageSpinner />
  if (status === 'anonymous') return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />
  return <>{children}</>
}

/** Route-level permission gate. The API enforces the same permissions server-side. */
function Guard({ permission, children }: { permission: Permission; children: ReactNode }) {
  const { can } = useAuth()
  const { t } = useTranslation()
  if (!can(permission))
    return <EmptyState icon={<ShieldAlert className="size-6" />} title={t('common.unauthorized')} className="py-24" />
  return <Suspense fallback={<FullPageSpinner />}>{children}</Suspense>
}

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route
          element={
            <RequireAuth>
              <AppShell />
            </RequireAuth>
          }
        >
          <Route index element={<Guard permission={Permissions.DashboardView}><DashboardPage /></Guard>} />
          <Route path="recovery" element={<Guard permission={Permissions.RecoveryView}><RecoveryPage /></Guard>} />
          <Route path="appointments" element={<Guard permission={Permissions.AppointmentsView}><AppointmentsPage /></Guard>} />
          <Route path="patients" element={<Guard permission={Permissions.PatientsView}><PatientsPage /></Guard>} />
          <Route path="patients/:id" element={<Guard permission={Permissions.PatientsView}><PatientProfilePage /></Guard>} />
          <Route path="treatment-plans" element={<Guard permission={Permissions.TreatmentView}><TreatmentPlansPage /></Guard>} />
          <Route path="treatment-plans/:id" element={<Guard permission={Permissions.TreatmentView}><PlanDetailPage /></Guard>} />
          <Route path="dental-chart" element={<Guard permission={Permissions.ChartView}><DentalChartPage /></Guard>} />
          <Route path="invoices" element={<Guard permission={Permissions.BillingView}><InvoicesPage /></Guard>} />
          <Route path="invoices/:id" element={<Guard permission={Permissions.BillingView}><InvoiceDetailPage /></Guard>} />
          <Route path="payments" element={<Guard permission={Permissions.BillingView}><PaymentsPage /></Guard>} />
          <Route path="reports" element={<Guard permission={Permissions.ReportsView}><ReportsPage /></Guard>} />
          <Route path="messages" element={<Guard permission={Permissions.MessagesView}><MessagesPage /></Guard>} />
          <Route path="assistant" element={<Guard permission={Permissions.AiUse}><AssistantPage /></Guard>} />
          <Route path="staff" element={<Guard permission={Permissions.StaffManage}><StaffPage /></Guard>} />
          <Route path="settings" element={<Guard permission={Permissions.SettingsManage}><SettingsPage /></Guard>} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Route>
      </Routes>
    </BrowserRouter>
  )
}
