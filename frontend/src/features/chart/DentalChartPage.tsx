import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { Smile, UserRound } from 'lucide-react'
import { api } from '@/lib/api'
import type { PatientDetail } from '@/lib/types'
import { Card, EmptyState, Field, PageHeader } from '@/components/ui/primitives'
import { Button } from '@/components/ui/Button'
import { PatientPicker, type PickedPatient } from '@/components/PatientPicker'
import { PatientChart } from './PatientChart'

export default function DentalChartPage() {
  const { t } = useTranslation()
  const [params, setParams] = useSearchParams()
  const [patient, setPatient] = useState<PickedPatient | null>(null)
  const id = params.get('patient')

  const fromUrl = useQuery({ queryKey: ['patient', id], queryFn: () => api.get<PatientDetail>(`/patients/${id}`), enabled: !!id && !patient })
  useEffect(() => {
    if (fromUrl.data) setPatient({ id: fromUrl.data.id, fullName: fromUrl.data.fullName, fullNameAr: fromUrl.data.fullNameAr, phone: fromUrl.data.phone })
  }, [fromUrl.data])

  const choose = (p: PickedPatient | null) => {
    setPatient(p)
    if (p) setParams({ patient: p.id }, { replace: true })
    else setParams({}, { replace: true })
  }

  return (
    <div className="space-y-5">
      <PageHeader
        title={t('chart.title')}
        actions={patient && <Link to={`/patients/${patient.id}`}><Button variant="outline" icon={<UserRound className="size-4" />}>{t('recovery.viewPatient')}</Button></Link>}
      />
      <Card className="p-4">
        <Field label={t('common.patient')} className="max-w-md"><PatientPicker value={patient} onChange={choose} /></Field>
      </Card>
      {patient ? <PatientChart key={patient.id} patient={patient} /> : (
        <Card><EmptyState icon={<Smile className="size-6" />} title={t('chart.selectPatient')} /></Card>
      )}
    </div>
  )
}
