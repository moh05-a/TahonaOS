import { useEffect, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { useQuery, keepPreviousData } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { AlertTriangle, Download, Search, UserPlus, Users } from 'lucide-react'
import { api } from '@/lib/api'
import { date, money, relative } from '@/lib/format'
import { displayName, downloadCsv, Permissions } from '@/lib/utils'
import { useDentists } from '@/lib/queries'
import type { Paged, PatientListItem, PatientStatus } from '@/lib/types'
import { useAuth } from '@/auth/AuthProvider'
import { Avatar, Badge, Card, EmptyState, ErrorState, Input, LoadingBlock, PageHeader, Pagination, Select } from '@/components/ui/primitives'
import { Button } from '@/components/ui/Button'
import { Table, THead, Td, Th, Tr } from '@/components/ui/Table'
import { PatientFormDialog } from './PatientFormDialog'

export default function PatientsPage() {
  const { t } = useTranslation()
  const { can } = useAuth()
  const navigate = useNavigate()
  const dentists = useDentists()
  const [params, setParams] = useSearchParams()
  const [search, setSearch] = useState(params.get('q') ?? '')
  const [debounced, setDebounced] = useState(search)
  const [status, setStatus] = useState<PatientStatus | ''>('')
  const [dentistId, setDentistId] = useState('')
  const [sort, setSort] = useState('registered')
  const [page, setPage] = useState(1)
  const [creating, setCreating] = useState(params.get('new') === '1')

  useEffect(() => {
    const id = setTimeout(() => { setDebounced(search); setPage(1) }, 200)
    return () => clearTimeout(id)
  }, [search])

  const query = useQuery({
    queryKey: ['patients', debounced, status, dentistId, sort, page],
    queryFn: () => api.get<Paged<PatientListItem>>('/patients', {
      search: debounced, status, dentistId, sort: sort === 'registered' ? undefined : sort, desc: sort !== 'name', page, pageSize: 25,
    }),
    placeholderData: keepPreviousData,
  })

  const exportCsv = () => downloadCsv('patients.csv',
    ['File', 'Name', 'Arabic name', 'Phone', 'Status', 'Last visit', 'Balance', 'Open treatment'],
    (query.data?.items ?? []).map((p) => [p.fileNumber, p.fullName, p.fullNameAr, p.phone, p.status, p.lastVisitAt?.slice(0, 10), p.outstandingBalance, p.openTreatmentValue]))

  return (
    <div className="space-y-5">
      <PageHeader
        title={t('patients.title')}
        subtitle={query.data ? t('common.showing', { count: query.data.totalCount }) : t('patients.subtitle')}
        actions={
          <>
            <Button variant="outline" icon={<Download className="size-4" />} onClick={exportCsv} disabled={!query.data?.items.length}>{t('common.export')}</Button>
            {can(Permissions.PatientsCreate) && <Button icon={<UserPlus className="size-4" />} onClick={() => setCreating(true)}>{t('patients.new')}</Button>}
          </>
        }
      />

      <Card>
        <div className="flex flex-wrap items-center gap-3 border-b border-line p-4">
          <div className="relative min-w-60 flex-1">
            <Search className="pointer-events-none absolute start-3 top-1/2 size-4 -translate-y-1/2 text-ink-3" />
            <Input autoFocus value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t('common.searchPlaceholder')} className="ps-9" />
          </div>
          <Select value={status} onChange={(e) => { setStatus(e.target.value as PatientStatus | ''); setPage(1) }} className="w-40">
            <option value="">{t('common.status')}: {t('common.all')}</option>
            {(['Active', 'Inactive', 'Archived'] as const).map((s) => <option key={s} value={s}>{t(`enum.PatientStatus.${s}`)}</option>)}
          </Select>
          <Select value={dentistId} onChange={(e) => { setDentistId(e.target.value); setPage(1) }} className="w-48">
            <option value="">{t('appointments.allDentists')}</option>
            {dentists.data?.map((d) => <option key={d.id} value={d.id}>{d.fullName}</option>)}
          </Select>
          <Select value={sort} onChange={(e) => setSort(e.target.value)} className="w-44">
            <option value="registered">{t('patients.registered')}</option>
            <option value="lastVisit">{t('patients.lastVisit')}</option>
            <option value="balance">{t('patients.balance')}</option>
            <option value="name">{t('common.name')}</option>
          </Select>
        </div>

        {query.error ? <ErrorState error={query.error} onRetry={() => void query.refetch()} />
          : query.isLoading ? <LoadingBlock rows={8} />
          : !query.data?.items.length ? (
            <EmptyState icon={<Users className="size-6" />} title={t('patients.empty')} body={t('patients.emptyBody')}
              action={can(Permissions.PatientsCreate) && <Button icon={<UserPlus className="size-4" />} onClick={() => setCreating(true)}>{t('patients.new')}</Button>} />
          ) : (
            <>
              <Table>
                <THead>
                  <tr>
                    <Th>{t('common.patient')}</Th>
                    <Th>{t('common.phone')}</Th>
                    <Th>{t('patients.lastVisit')}</Th>
                    <Th>{t('patients.nextAppointment')}</Th>
                    <Th className="text-end">{t('patients.openTreatment')}</Th>
                    <Th className="text-end">{t('patients.balance')}</Th>
                    <Th>{t('common.status')}</Th>
                  </tr>
                </THead>
                <tbody>
                  {query.data.items.map((p) => (
                    <Tr key={p.id} onClick={() => navigate(`/patients/${p.id}`)}>
                      <Td>
                        <div className="flex items-center gap-3">
                          <Avatar name={p.fullName} />
                          <div className="min-w-0">
                            <p className="flex items-center gap-1.5 truncate font-medium text-ink">
                              {displayName(p.fullName, p.fullNameAr)}
                              {p.hasMedicalAlert && <AlertTriangle className="size-3.5 text-critical" aria-label={t('patients.medicalAlert')} />}
                            </p>
                            <p className="text-xs text-ink-3">{p.fileNumber}{p.age != null && ` · ${p.age}y`}{p.primaryDentistName && ` · ${p.primaryDentistName}`}</p>
                          </div>
                        </div>
                      </Td>
                      <Td className="ltr text-ink-2">{p.phone}</Td>
                      <Td className="text-ink-2">{p.lastVisitAt ? <span title={date(p.lastVisitAt)}>{relative(p.lastVisitAt)}</span> : '—'}</Td>
                      <Td className="text-ink-2">{p.nextAppointmentAt ? date(p.nextAppointmentAt) : '—'}</Td>
                      <Td className="tabular text-end text-ink-2">{p.openTreatmentValue > 0 ? money(p.openTreatmentValue) : '—'}</Td>
                      <Td className="tabular text-end">{p.outstandingBalance > 0 ? <span className="font-medium text-critical-ink">{money(p.outstandingBalance)}</span> : '—'}</Td>
                      <Td><Badge tone={p.status === 'Active' ? 'good' : 'neutral'} dot>{t(`enum.PatientStatus.${p.status}`)}</Badge></Td>
                    </Tr>
                  ))}
                </tbody>
              </Table>
              <Pagination page={page} totalPages={query.data.totalPages} onChange={setPage} />
            </>
          )}
      </Card>

      <PatientFormDialog
        open={creating}
        onClose={() => { setCreating(false); if (params.get('new')) { params.delete('new'); setParams(params, { replace: true }) } }}
        onSaved={(id) => navigate(`/patients/${id}`)}
      />
    </div>
  )
}
