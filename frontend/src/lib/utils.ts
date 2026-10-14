import { clsx, type ClassValue } from 'clsx'
import { twMerge } from 'tailwind-merge'
import i18n from '@/i18n'

export const cn = (...inputs: ClassValue[]) => twMerge(clsx(inputs))

/** Shows the Arabic name in Arabic UI when available. */
export const displayName = (en: string, ar?: string | null) => (i18n.language === 'ar' && ar ? ar : en)

export const Permissions = {
  DashboardView: 'dashboard.view',
  PatientsView: 'patients.view', PatientsCreate: 'patients.create', PatientsEdit: 'patients.edit', PatientsDelete: 'patients.delete',
  AppointmentsView: 'appointments.view', AppointmentsManage: 'appointments.manage',
  ChartView: 'chart.view', ChartEdit: 'chart.edit',
  TreatmentView: 'treatment.view', TreatmentManage: 'treatment.manage',
  ClinicalView: 'clinical.view', ClinicalManage: 'clinical.manage',
  BillingView: 'billing.view', InvoicesManage: 'invoices.manage', PaymentsManage: 'payments.manage', PaymentsRefund: 'payments.refund',
  ExpensesView: 'expenses.view', ExpensesManage: 'expenses.manage',
  RecoveryView: 'recovery.view', RecoveryManage: 'recovery.manage',
  MessagesView: 'messages.view', MessagesSend: 'messages.send', TemplatesManage: 'templates.manage',
  DocumentsView: 'documents.view', DocumentsManage: 'documents.manage',
  ReportsView: 'reports.view', AiUse: 'ai.use',
  StaffManage: 'staff.manage', SettingsManage: 'settings.manage', AuditView: 'audit.view',
  DataImport: 'data.import', DataExport: 'data.export',
} as const

export type Permission = (typeof Permissions)[keyof typeof Permissions]

/** Download helper for authenticated blobs. */
export function saveBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = fileName
  a.click()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}

/** CSV export for any table. */
export function downloadCsv(fileName: string, header: string[], rows: (string | number | null | undefined)[][]) {
  const esc = (v: string | number | null | undefined) => {
    const s = v == null ? '' : String(v)
    return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s
  }
  const csv = '﻿' + [header, ...rows].map((r) => r.map(esc).join(',')).join('\n')
  saveBlob(new Blob([csv], { type: 'text/csv;charset=utf-8' }), fileName)
}
