import { useEffect, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { useQuery, keepPreviousData } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { Download, FileText, Plus, Search } from 'lucide-react'
import { api } from '@/lib/api'
import { date, money } from '@/lib/format'
import { displayName, downloadCsv, Permissions } from '@/lib/utils'
import type { InvoiceListItem, InvoiceStatus, Paged } from '@/lib/types'
import { useAuth } from '@/auth/AuthProvider'
import { Card, EmptyState, ErrorState, Input, LoadingBlock, PageHeader, Pagination, Segmented, Select } from '@/components/ui/primitives'
import { Button } from '@/components/ui/Button'
import { Table, THead, Td, Th, Tr } from '@/components/ui/Table'
import { InvoiceStatusBadge } from '@/components/StatusBadge'
import { InvoiceFormDialog } from './InvoiceFormDialog'

type Scope = 'all' | 'open' | 'overdue'

export default function InvoicesPage() {
  const { t } = useTranslation()
  const { can } = useAuth()
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const [scope, setScope] = useState<Scope>(params.get('overdue') ? 'overdue' : 'all')
  const [status, setStatus] = useState<InvoiceStatus | ''>('')
  const [search, setSearch] = useState('')
  const [debounced, setDebounced] = useState('')
  const [page, setPage] = useState(1)
  const [creating, setCreating] = useState(false)

  useEffect(() => {
    const id = setTimeout(() => { setDebounced(search); setPage(1) }, 200)
    return () => clearTimeout(id)
  }, [search])

  const q = useQuery({
    queryKey: ['invoices', scope, status, debounced, page],
    queryFn: () => api.get<Paged<InvoiceListItem>>('/invoices', {
      search: debounced, status, openOnly: scope === 'open' || undefined, overdueOnly: scope === 'overdue' || undefined, page, pageSize: 25,
    }),
    placeholderData: keepPreviousData,
  })

  const exportCsv = () => downloadCsv('invoices.csv', ['Invoice', 'Patient', 'Issued', 'Due', 'Total', 'Paid', 'Balance', 'Status'],
    (q.data?.items ?? []).map((i) => [i.invoiceNumber, i.patientName, i.issueDate.slice(0, 10), i.dueDate.slice(0, 10), i.total, i.amountPaid, i.balance, i.isOverdue ? 'Overdue' : i.status]))

  return (
    <div className="space-y-5">
      <PageHeader
        title={t('billing.invoices')}
        subtitle={q.data && t('common.showing', { count: q.data.totalCount })}
        actions={
          <>
            <Button variant="outline" icon={<Download className="size-4" />} onClick={exportCsv} disabled={!q.data?.items.length}>{t('common.export')}</Button>
            {can(Permissions.InvoicesManage) && <Button icon={<Plus className="size-4" />} onClick={() => setCreating(true)}>{t('billing.newInvoice')}</Button>}
          </>
        }
      />
      <Card>
        <div className="flex flex-wrap items-center gap-3 border-b border-line p-4">
          <Segmented<Scope> value={scope} onChange={(v) => { setScope(v); setPage(1) }} items={[
            { value: 'all', label: t('common.all') }, { value: 'open', label: t('billing.openOnly') }, { value: 'overdue', label: t('billing.overdueOnly') },
          ]} />
          <Select value={status} onChange={(e) => { setStatus(e.target.value as InvoiceStatus | ''); setPage(1) }} className="w-44">
            <option value="">{t('common.status')}: {t('common.all')}</option>
            {(['Draft', 'Issued', 'PartiallyPaid', 'Paid', 'Void', 'Refunded'] as const).map((s) => <option key={s} value={s}>{t(`enum.InvoiceStatus.${s}`)}</option>)}
          </Select>
          <div className="relative ms-auto w-full sm:w-72">
            <Search className="pointer-events-none absolute start-3 top-1/2 size-4 -translate-y-1/2 text-ink-3" />
            <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t('common.search')} className="ps-9" />
          </div>
        </div>
        {q.error ? <ErrorState error={q.error} onRetry={() => void q.refetch()} /> : q.isLoading ? <LoadingBlock rows={8} /> : !q.data?.items.length ? (
          <EmptyState icon={<FileText className="size-6" />} title={t('billing.empty')} />
        ) : (
          <>
            <Table>
              <THead>
                <tr>
                  <Th>{t('billing.invoice')}</Th><Th>{t('common.patient')}</Th><Th>{t('billing.issueDate')}</Th><Th>{t('billing.dueDate')}</Th>
                  <Th className="text-end">{t('common.total')}</Th><Th className="text-end">{t('billing.paid')}</Th><Th className="text-end">{t('billing.balance')}</Th><Th>{t('common.status')}</Th>
                </tr>
              </THead>
              <tbody>
                {q.data.items.map((i) => (
                  <Tr key={i.id} onClick={() => navigate(`/invoices/${i.id}`)}>
                    <Td><p className="font-medium text-ink">{i.invoiceNumber}</p>{i.planNumber && <p className="text-xs text-ink-3">{i.planNumber}</p>}</Td>
                    <Td>{displayName(i.patientName, i.patientNameAr)}</Td>
                    <Td className="text-ink-2">{date(i.issueDate)}</Td>
                    <Td className="text-ink-2">{date(i.dueDate)}{i.isOverdue && <span className="ms-1.5 text-xs text-critical-ink">{t('billing.overdueDays', { count: i.daysOverdue })}</span>}</Td>
                    <Td className="tabular text-end">{money(i.total)}</Td>
                    <Td className="tabular text-end text-ink-2">{money(i.amountPaid)}</Td>
                    <Td className="tabular text-end">{i.balance > 0 ? <span className="font-semibold text-critical-ink">{money(i.balance)}</span> : '—'}</Td>
                    <Td><InvoiceStatusBadge status={i.status} overdue={i.isOverdue} /></Td>
                  </Tr>
                ))}
              </tbody>
            </Table>
            <Pagination page={page} totalPages={q.data.totalPages} onChange={setPage} />
          </>
        )}
      </Card>
      <InvoiceFormDialog open={creating} onClose={() => setCreating(false)} onSaved={(id) => navigate(`/invoices/${id}`)} />
    </div>
  )
}
