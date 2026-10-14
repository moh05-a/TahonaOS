import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient, keepPreviousData } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { Ban, CreditCard, Download, MessageCircle, Pencil, Plus, Receipt, RotateCcw, Search, Sparkles, Trash2, TrendingUp, Wallet } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { clinicLocalToUtc, date, dateTime, isoDateInClinic, money, relative, utcToClinicLocal } from '@/lib/format'
import { displayName, downloadCsv, Permissions } from '@/lib/utils'
import type { Expense, ExpenseCategory, Paged, PatientBalance, PaymentListItem, PaymentMethod, PaymentSummary } from '@/lib/types'
import { useAuth } from '@/auth/AuthProvider'
import { Badge, Card, EmptyState, ErrorState, Field, Input, LoadingBlock, PageHeader, Pagination, Select, Skeleton, StatCard, Tabs } from '@/components/ui/primitives'
import { Button } from '@/components/ui/Button'
import { ConfirmDialog, Dialog } from '@/components/ui/Dialog'
import { Table, THead, Td, Th, Tr } from '@/components/ui/Table'
import { RecordPaymentDialog } from './RecordPaymentDialog'
import { PaymentRowActions } from './PaymentActions'
import { SendMessageDialog } from '@/features/messages/SendMessageDialog'
import type { PickedPatient } from '@/components/PatientPicker'

type Tab = 'payments' | 'outstanding' | 'expenses'
const methods: PaymentMethod[] = ['Cash', 'Card', 'CliQ', 'BankTransfer', 'Insurance', 'Other']

export default function PaymentsPage() {
  const { t } = useTranslation()
  const { can } = useAuth()
  const [tab, setTab] = useState<Tab>('payments')
  const [paying, setPaying] = useState<PickedPatient | null | undefined>(undefined)
  const summary = useQuery({ queryKey: ['payments', 'summary'], queryFn: () => api.get<PaymentSummary>('/payments/summary') })

  return (
    <div className="space-y-5">
      <PageHeader
        title={t('billing.payments')}
        actions={can(Permissions.PaymentsManage) && <Button icon={<Plus className="size-4" />} onClick={() => setPaying(null)}>{t('billing.recordPayment')}</Button>}
      />
      <div className="grid grid-cols-2 gap-4 xl:grid-cols-4">
        {!summary.data ? Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-28" />) : (
          <>
            <StatCard label={t('billing.collectedToday')} value={money(summary.data.today)} icon={<Wallet className="size-4" />} tone="good" />
            <StatCard label={t('billing.collectedMonth')} value={money(summary.data.thisMonth)} icon={<TrendingUp className="size-4" />}
              hint={summary.data.byMethod.map((m) => `${t(`enum.PaymentMethod.${m.method}`)} ${money(m.amount, { compact: true })}`).join(' · ')} />
            <StatCard label={t('billing.recoveredMonth')} value={money(summary.data.recoveredThisMonth)} icon={<Sparkles className="size-4" />} tone="recovery" />
            <StatCard label={t('billing.refundsMonth')} value={money(summary.data.refundsThisMonth)} icon={<RotateCcw className="size-4" />} tone="serious" />
          </>
        )}
      </div>
      <Tabs<Tab> value={tab} onChange={setTab} items={[
        { value: 'payments', label: t('billing.payments') },
        { value: 'outstanding', label: t('billing.outstanding') },
        { value: 'expenses', label: t('billing.expenses'), hidden: !can(Permissions.ExpensesView) },
      ]} />
      {tab === 'payments' && <PaymentsList />}
      {tab === 'outstanding' && <Outstanding onPay={(p) => setPaying(p)} />}
      {tab === 'expenses' && <Expenses />}
      <RecordPaymentDialog open={paying !== undefined} onClose={() => setPaying(undefined)} patient={paying ?? undefined} />
    </div>
  )
}

function PaymentsList() {
  const { t } = useTranslation()
  const [search, setSearch] = useState('')
  const [debounced, setDebounced] = useState('')
  const [method, setMethod] = useState<PaymentMethod | ''>('')
  const [recovered, setRecovered] = useState(false)
  const [cancelled, setCancelled] = useState(false)
  const [page, setPage] = useState(1)
  useEffect(() => { const id = setTimeout(() => { setDebounced(search); setPage(1) }, 200); return () => clearTimeout(id) }, [search])

  const q = useQuery({
    queryKey: ['payments', debounced, method, recovered, cancelled, page],
    queryFn: () => api.get<Paged<PaymentListItem>>('/payments', {
      search: debounced, method, recoveredOnly: recovered || undefined, cancelled: cancelled || undefined, page, pageSize: 25,
    }),
    placeholderData: keepPreviousData,
  })
  const exportCsv = () => downloadCsv('payments.csv', ['Receipt', 'Patient', 'Invoice', 'Type', 'Method', 'Amount', 'Date', 'Recovered'],
    (q.data?.items ?? []).map((p) => [p.receiptNumber, p.patientName, p.invoiceNumber, p.type, p.method, p.amount, p.paidAt, p.isRecovered ? 'yes' : '']))

  return (
    <Card>
      <div className="flex flex-wrap items-center gap-3 border-b border-line p-4">
        <div className="relative min-w-60 flex-1">
          <Search className="pointer-events-none absolute start-3 top-1/2 size-4 -translate-y-1/2 text-ink-3" />
          <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t('common.search')} className="ps-9" />
        </div>
        <Select value={method} onChange={(e) => { setMethod(e.target.value as PaymentMethod | ''); setPage(1) }} className="w-44">
          <option value="">{t('common.method')}: {t('common.all')}</option>
          {methods.map((m) => <option key={m} value={m}>{t(`enum.PaymentMethod.${m}`)}</option>)}
        </Select>
        <Button variant={recovered ? 'recovery' : 'outline'} size="sm" icon={<Sparkles className="size-3.5" />} onClick={() => { setRecovered((r) => !r); setPage(1) }}>{t('billing.recovered')}</Button>
        <Button variant={cancelled ? 'secondary' : 'outline'} size="sm" icon={<Ban className="size-3.5" />} aria-pressed={cancelled}
          onClick={() => { setCancelled((c) => !c); setPage(1) }}>{t('billing.cancelledEntries')}</Button>
        <Button variant="outline" size="sm" icon={<Download className="size-3.5" />} onClick={exportCsv}>{t('common.export')}</Button>
      </div>
      {cancelled && <p className="border-b border-line bg-surface-2 px-5 py-2.5 text-sm text-ink-2">{t('billing.cancelledEntriesHint')}</p>}
      {q.error ? <ErrorState error={q.error} /> : q.isLoading ? <LoadingBlock rows={8} /> : !q.data?.items.length ? <EmptyState icon={<Receipt className="size-6" />} title={t('common.noResults')} /> : (
        <>
          <Table>
            <THead><tr><Th>{t('billing.receipt')}</Th><Th>{t('common.patient')}</Th><Th>{t('billing.invoice')}</Th><Th>{t('common.date')}</Th><Th>{t('common.method')}</Th><Th className="text-end">{t('common.amount')}</Th><Th /></tr></THead>
            <tbody>
              {q.data.items.map((p) => (
                <Tr key={p.id}>
                  <Td className="font-medium">
                    {p.receiptNumber}{p.isRecovered && <Badge tone="recovery" className="ms-2"><Sparkles className="size-3" />{t('billing.recovered')}</Badge>}
                    {p.isCancelled && (
                      <>
                        <Badge tone="critical" className="ms-2">{t('billing.cancelled')}</Badge>
                        <span className="mt-1 block max-w-xs text-xs font-normal text-ink-3">
                          {t('billing.cancelledBy', { name: p.cancelledBy ?? '—', date: p.cancelledAt ? dateTime(p.cancelledAt) : '' })}
                          {p.cancelReason && <span className="block text-ink-2" dir="auto">{p.cancelReason}</span>}
                        </span>
                      </>
                    )}
                  </Td>
                  <Td><Link to={`/patients/${p.patientId}`} className="hover:underline">{p.patientName}</Link></Td>
                  <Td>{p.invoiceId ? <Link to={`/invoices/${p.invoiceId}`} className="text-primary-ink hover:underline">{p.invoiceNumber}</Link> : '—'}</Td>
                  <Td className="text-ink-2">{dateTime(p.paidAt)}{p.receivedBy && <span className="block text-xs text-ink-3">{p.receivedBy}</span>}</Td>
                  <Td>{t(`enum.PaymentMethod.${p.method}`)}{p.type === 'Refund' && <Badge tone="serious" className="ms-2">{t('billing.refund')}</Badge>}</Td>
                  <Td className={`tabular text-end font-medium ${p.isCancelled ? 'text-ink-3 line-through' : p.type === 'Refund' ? 'text-critical-ink' : 'text-ink'}`}>{p.type === 'Refund' ? '−' : ''}{money(p.amount)}</Td>
                  <Td className="text-end"><PaymentRowActions payment={p} /></Td>
                </Tr>
              ))}
            </tbody>
          </Table>
          <Pagination page={page} totalPages={q.data.totalPages} onChange={setPage} />
        </>
      )}
    </Card>
  )
}

function Outstanding({ onPay }: { onPay: (p: PickedPatient) => void }) {
  const { t } = useTranslation()
  const { can } = useAuth()
  const navigate = useNavigate()
  const [messageTo, setMessageTo] = useState<PickedPatient | null>(null)
  const q = useQuery({ queryKey: ['invoices', 'outstanding'], queryFn: () => api.get<PatientBalance[]>('/invoices/outstanding', { take: 200 }) })
  const total = q.data?.reduce((s, b) => s + b.balance, 0) ?? 0
  return (
    <Card>
      <div className="flex items-center justify-between border-b border-line px-5 py-3 text-sm">
        <span className="text-ink-2">{t('common.showing', { count: q.data?.length ?? 0 })}</span>
        <span className="font-semibold text-ink">{t('common.total')}: <span className="tabular text-critical-ink">{money(total)}</span></span>
      </div>
      {q.error ? <ErrorState error={q.error} /> : q.isLoading ? <LoadingBlock rows={6} /> : !q.data?.length ? <EmptyState icon={<CreditCard className="size-6" />} title={t('common.noResults')} /> : (
        <Table>
          <THead><tr><Th>{t('common.patient')}</Th><Th className="text-end">{t('billing.balance')}</Th><Th>{t('billing.invoices')}</Th><Th>{t('billing.oldestDue')}</Th><Th>{t('billing.lastPayment')}</Th><Th /></tr></THead>
          <tbody>
            {q.data.map((b) => {
              const p = { id: b.patientId, fullName: b.patientName, fullNameAr: b.patientNameAr, phone: b.phone }
              return (
                <Tr key={b.patientId} onClick={() => navigate(`/patients/${b.patientId}?tab=invoices`)}>
                  <Td><p className="font-medium text-ink">{displayName(b.patientName, b.patientNameAr)}</p><p className="ltr text-xs text-ink-3">{b.phone}</p></Td>
                  <Td className="tabular text-end font-semibold text-critical-ink">{money(b.balance)}</Td>
                  <Td>{b.openInvoices}</Td>
                  <Td>{date(b.oldestDueDate)}{b.daysOverdue > 0 && <Badge tone="critical" className="ms-2">{t('billing.overdueDays', { count: b.daysOverdue })}</Badge>}</Td>
                  <Td className="text-ink-2">{b.lastPaymentAt ? relative(b.lastPaymentAt) : '—'}</Td>
                  <Td className="text-end" onClick={(e) => e.stopPropagation()}>
                    <div className="flex justify-end gap-1.5">
                      {can(Permissions.MessagesSend) && <Button size="sm" variant="ghost" icon={<MessageCircle className="size-3.5" />} onClick={() => setMessageTo(p)}>{t('common.send')}</Button>}
                      {can(Permissions.PaymentsManage) && <Button size="sm" variant="outline" onClick={() => onPay(p)}>{t('billing.recordPayment')}</Button>}
                    </div>
                  </Td>
                </Tr>
              )
            })}
          </tbody>
        </Table>
      )}
      <SendMessageDialog open={!!messageTo} onClose={() => setMessageTo(null)} patient={messageTo} />
    </Card>
  )
}

const expenseCategories: ExpenseCategory[] = ['Rent', 'Salaries', 'Supplies', 'Lab', 'Equipment', 'Utilities', 'Marketing', 'Maintenance', 'Other']

function Expenses() {
  const { t } = useTranslation()
  const { can } = useAuth()
  const qc = useQueryClient()
  const [category, setCategory] = useState<ExpenseCategory | ''>('')
  const [page, setPage] = useState(1)
  const [editing, setEditing] = useState<Expense | null | undefined>(undefined)
  const [deleting, setDeleting] = useState<Expense | null>(null)
  const q = useQuery({
    queryKey: ['expenses', category, page],
    queryFn: () => api.get<Paged<Expense>>('/expenses', { category, page, pageSize: 25 }),
    placeholderData: keepPreviousData,
  })
  const remove = useMutation({
    mutationFn: (id: string) => api.delete(`/expenses/${id}`),
    onSuccess: () => { setDeleting(null); void qc.invalidateQueries({ queryKey: ['expenses'] }) },
    onError: (e) => toast.error(errorMessage(e)),
  })
  return (
    <Card>
      <div className="flex flex-wrap items-center gap-3 border-b border-line p-4">
        <Select value={category} onChange={(e) => { setCategory(e.target.value as ExpenseCategory | ''); setPage(1) }} className="w-48">
          <option value="">{t('common.category')}: {t('common.all')}</option>
          {expenseCategories.map((c) => <option key={c} value={c}>{t(`enum.ExpenseCategory.${c}`)}</option>)}
        </Select>
        {can(Permissions.ExpensesManage) && <Button className="ms-auto" icon={<Plus className="size-4" />} onClick={() => setEditing(null)}>{t('billing.newExpense')}</Button>}
      </div>
      {q.error ? <ErrorState error={q.error} /> : q.isLoading ? <LoadingBlock rows={6} /> : !q.data?.items.length ? <EmptyState title={t('common.noResults')} /> : (
        <>
          <Table>
            <THead><tr><Th>{t('common.date')}</Th><Th>{t('common.description')}</Th><Th>{t('common.category')}</Th><Th>{t('billing.vendor')}</Th><Th className="text-end">{t('common.amount')}</Th><Th /></tr></THead>
            <tbody>
              {q.data.items.map((e) => (
                <Tr key={e.id}>
                  <Td>{date(e.expenseDate)}</Td>
                  <Td className="font-medium">{e.description}</Td>
                  <Td><Badge>{t(`enum.ExpenseCategory.${e.category}`)}</Badge></Td>
                  <Td className="text-ink-2">{e.vendor ?? '—'}</Td>
                  <Td className="tabular text-end font-medium">{money(e.amount)}</Td>
                  <Td className="text-end">
                    {can(Permissions.ExpensesManage) && (
                      <div className="flex justify-end gap-1">
                        <Button size="icon" variant="ghost" className="size-8" onClick={() => setEditing(e)} aria-label={t('common.edit')}><Pencil className="size-3.5" /></Button>
                        <Button size="icon" variant="ghost" className="size-8" onClick={() => setDeleting(e)} aria-label={t('common.delete')}><Trash2 className="size-3.5" /></Button>
                      </div>
                    )}
                  </Td>
                </Tr>
              ))}
            </tbody>
          </Table>
          <Pagination page={page} totalPages={q.data.totalPages} onChange={setPage} />
        </>
      )}
      <ExpenseDialog expense={editing} onClose={() => setEditing(undefined)} />
      <ConfirmDialog open={!!deleting} onClose={() => setDeleting(null)} onConfirm={() => deleting && remove.mutate(deleting.id)} danger loading={remove.isPending}
        title={t('common.delete')} body={t('common.confirmDelete')} />
    </Card>
  )
}

function ExpenseDialog({ expense, onClose }: { expense: Expense | null | undefined; onClose: () => void }) {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const [form, setForm] = useState({ category: 'Supplies' as ExpenseCategory, description: '', amount: '', date: isoDateInClinic(), vendor: '', method: 'Cash' as PaymentMethod, reference: '' })
  useEffect(() => {
    if (expense === undefined) return
    setForm(expense ? {
      category: expense.category, description: expense.description, amount: String(expense.amount), date: utcToClinicLocal(expense.expenseDate).slice(0, 10),
      vendor: expense.vendor ?? '', method: expense.paymentMethod, reference: expense.reference ?? '',
    } : { category: 'Supplies', description: '', amount: '', date: isoDateInClinic(), vendor: '', method: 'Cash', reference: '' })
  }, [expense])

  const save = useMutation({
    mutationFn: () => {
      const body = {
        category: form.category, description: form.description, amount: Number(form.amount), expenseDate: clinicLocalToUtc(`${form.date}T12:00`),
        vendor: form.vendor || null, paymentMethod: form.method, reference: form.reference || null,
      }
      return expense ? api.put(`/expenses/${expense.id}`, body) : api.post('/expenses', body)
    },
    onSuccess: () => { toast.success(t('common.saved')); void qc.invalidateQueries({ queryKey: ['expenses'] }); onClose() },
    onError: (e) => toast.error(errorMessage(e)),
  })
  const set = <K extends keyof typeof form>(k: K, v: (typeof form)[K]) => setForm((f) => ({ ...f, [k]: v }))
  return (
    <Dialog open={expense !== undefined} onClose={onClose} title={expense ? t('common.edit') : t('billing.newExpense')}
      footer={<><Button variant="outline" onClick={onClose}>{t('common.cancel')}</Button>
        <Button loading={save.isPending} disabled={!form.description.trim() || !(Number(form.amount) > 0)} onClick={() => save.mutate()}>{t('common.save')}</Button></>}>
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label={t('common.description')} required className="sm:col-span-2"><Input value={form.description} onChange={(e) => set('description', e.target.value)} maxLength={300} /></Field>
        <Field label={t('common.category')}><Select value={form.category} onChange={(e) => set('category', e.target.value as ExpenseCategory)}>{expenseCategories.map((c) => <option key={c} value={c}>{t(`enum.ExpenseCategory.${c}`)}</option>)}</Select></Field>
        <Field label={t('common.amount')} required><Input type="number" min={0} step="0.001" value={form.amount} onChange={(e) => set('amount', e.target.value)} /></Field>
        <Field label={t('common.date')}><Input type="date" max={isoDateInClinic()} value={form.date} onChange={(e) => set('date', e.target.value)} /></Field>
        <Field label={t('common.method')}><Select value={form.method} onChange={(e) => set('method', e.target.value as PaymentMethod)}>{methods.map((m) => <option key={m} value={m}>{t(`enum.PaymentMethod.${m}`)}</option>)}</Select></Field>
        <Field label={t('billing.vendor')}><Input value={form.vendor} onChange={(e) => set('vendor', e.target.value)} maxLength={150} /></Field>
        <Field label={t('common.reference')}><Input value={form.reference} onChange={(e) => set('reference', e.target.value)} maxLength={100} /></Field>
      </div>
    </Dialog>
  )
}
