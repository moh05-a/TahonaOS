import { useEffect } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { api, ApiError, errorMessage } from '@/lib/api'
import { useDentists } from '@/lib/queries'
import type { PatientDetail } from '@/lib/types'
import { Dialog } from '@/components/ui/Dialog'
import { Button } from '@/components/ui/Button'
import { Field, Input, Select, Textarea } from '@/components/ui/primitives'

const phoneDigits = (s: string) => s.replace(/\D/g, '').replace(/^00962|^962/, '').replace(/^0+/, '')

const schema = z.object({
  fullName: z.string().trim().min(1).max(150),
  fullNameAr: z.string().max(150).optional(),
  dateOfBirth: z.string().optional().refine((v) => !v || new Date(v) <= new Date(), 'Date of birth must be in the past.'),
  gender: z.enum(['Male', 'Female', '']).optional(),
  phone: z.string().trim().min(1).refine((v) => phoneDigits(v).length >= 7 && phoneDigits(v).length <= 15, 'Enter a valid phone number.'),
  whatsAppNumber: z.string().optional().refine((v) => !v || (phoneDigits(v).length >= 7 && phoneDigits(v).length <= 15), 'Enter a valid WhatsApp number.'),
  email: z.union([z.literal(''), z.string().email()]).optional(),
  address: z.string().max(300).optional(),
  city: z.string().max(80).optional(),
  nationalId: z.string().max(40).optional(),
  emergencyContactName: z.string().max(150).optional(),
  emergencyContactPhone: z.string().max(30).optional(),
  emergencyContactRelation: z.string().max(50).optional(),
  medicalNotes: z.string().max(4000).optional(),
  allergies: z.string().max(1000).optional(),
  medicalConditions: z.string().max(1000).optional(),
  currentMedications: z.string().max(1000).optional(),
  insuranceProvider: z.string().max(120).optional(),
  insurancePolicyNumber: z.string().max(60).optional(),
  insuranceCoveragePercent: z.string().optional().refine((v) => !v || (Number(v) >= 0 && Number(v) <= 100), '0–100'),
  preferredLanguage: z.enum(['ar', 'en']),
  status: z.enum(['Active', 'Inactive', 'Archived']),
  referralSource: z.string().max(80).optional(),
  primaryDentistId: z.string().optional(),
})
type FormValues = z.infer<typeof schema>

const empty: FormValues = {
  fullName: '', fullNameAr: '', dateOfBirth: '', gender: '', phone: '', whatsAppNumber: '', email: '', address: '', city: 'Amman', nationalId: '',
  emergencyContactName: '', emergencyContactPhone: '', emergencyContactRelation: '', medicalNotes: '', allergies: '', medicalConditions: '',
  currentMedications: '', insuranceProvider: '', insurancePolicyNumber: '', insuranceCoveragePercent: '', preferredLanguage: 'ar', status: 'Active',
  referralSource: '', primaryDentistId: '',
}

const blank = (v?: string) => (v && v.trim() ? v.trim() : null)

export function PatientFormDialog({ open, onClose, patient, onSaved }: {
  open: boolean; onClose: () => void; patient?: PatientDetail | null; onSaved?: (id: string) => void
}) {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const dentists = useDentists()
  const { register, handleSubmit, reset, setError, formState: { errors } } = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: empty })

  useEffect(() => {
    if (!open) return
    reset(patient ? {
      ...empty,
      ...Object.fromEntries(Object.entries(patient).map(([k, v]) => [k, v ?? ''])),
      gender: patient.gender ?? '',
      insuranceCoveragePercent: patient.insuranceCoveragePercent?.toString() ?? '',
      preferredLanguage: patient.preferredLanguage === 'en' ? 'en' : 'ar',
    } as FormValues : empty)
  }, [open, patient, reset])

  const save = useMutation({
    mutationFn: (v: FormValues) => {
      const body = {
        fullName: v.fullName.trim(), fullNameAr: blank(v.fullNameAr), dateOfBirth: blank(v.dateOfBirth), gender: v.gender || null,
        phone: v.phone.trim(), whatsAppNumber: blank(v.whatsAppNumber), email: blank(v.email), address: blank(v.address), city: blank(v.city),
        nationalId: blank(v.nationalId), emergencyContactName: blank(v.emergencyContactName), emergencyContactPhone: blank(v.emergencyContactPhone),
        emergencyContactRelation: blank(v.emergencyContactRelation), medicalNotes: blank(v.medicalNotes), allergies: blank(v.allergies),
        medicalConditions: blank(v.medicalConditions), currentMedications: blank(v.currentMedications), insuranceProvider: blank(v.insuranceProvider),
        insurancePolicyNumber: blank(v.insurancePolicyNumber), insuranceCoveragePercent: v.insuranceCoveragePercent ? Number(v.insuranceCoveragePercent) : null,
        preferredLanguage: v.preferredLanguage, status: v.status, referralSource: blank(v.referralSource), primaryDentistId: v.primaryDentistId || null,
      }
      return patient ? api.put(`/patients/${patient.id}`, body).then(() => patient.id) : api.post<{ id: string }>('/patients', body).then((r) => r.id)
    },
    onSuccess: (id) => {
      toast.success(patient ? t('common.updated') : t('patients.created'))
      void qc.invalidateQueries({ queryKey: ['patients'] })
      void qc.invalidateQueries({ queryKey: ['patient', id] })
      onSaved?.(id)
      onClose()
    },
    onError: (e) => {
      if (e instanceof ApiError && Object.keys(e.fieldErrors).length) {
        Object.entries(e.fieldErrors).forEach(([k, v]) => setError(k as keyof FormValues, { message: v[0] }))
      } else toast.error(errorMessage(e))
    },
  })

  const err = (k: keyof FormValues) => errors[k]?.message && (errors[k]?.message === 'Required' || errors[k]?.type === 'too_small' ? t('common.required') : errors[k]?.message)
  const section = (title: string) => <h3 className="col-span-full mt-2 text-xs font-semibold tracking-wide text-ink-3 uppercase">{title}</h3>

  return (
    <Dialog
      open={open}
      onClose={onClose}
      size="xl"
      title={patient ? t('patients.edit') : t('patients.new')}
      footer={
        <>
          <Button variant="outline" onClick={onClose}>{t('common.cancel')}</Button>
          <Button onClick={handleSubmit((v) => save.mutate(v))} loading={save.isPending}>{t('common.save')}</Button>
        </>
      }
    >
      <form className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3" onSubmit={handleSubmit((v) => save.mutate(v))} noValidate>
        {section(t('patients.sections.personal'))}
        <Field label={t('patients.fields.fullName')} error={err('fullName')} required><Input {...register('fullName')} invalid={!!errors.fullName} /></Field>
        <Field label={t('patients.fields.fullNameAr')} error={err('fullNameAr')}><Input dir="rtl" {...register('fullNameAr')} /></Field>
        <Field label={t('patients.fields.dateOfBirth')} error={err('dateOfBirth')}><Input type="date" {...register('dateOfBirth')} invalid={!!errors.dateOfBirth} /></Field>
        <Field label={t('patients.fields.gender')}>
          <Select {...register('gender')}>
            <option value="">—</option>
            <option value="Male">{t('enum.Gender.Male')}</option>
            <option value="Female">{t('enum.Gender.Female')}</option>
          </Select>
        </Field>
        <Field label={t('patients.fields.nationalId')}><Input {...register('nationalId')} /></Field>
        <Field label={t('patients.fields.preferredLanguage')}>
          <Select {...register('preferredLanguage')}><option value="ar">العربية</option><option value="en">English</option></Select>
        </Field>

        {section(t('patients.sections.contact'))}
        <Field label={t('patients.fields.phone')} error={err('phone')} required><Input className="ltr" inputMode="tel" placeholder="07X XXX XXXX" {...register('phone')} invalid={!!errors.phone} /></Field>
        <Field label={t('patients.fields.whatsApp')} error={err('whatsAppNumber')} hint={t('common.optional')}><Input className="ltr" inputMode="tel" {...register('whatsAppNumber')} invalid={!!errors.whatsAppNumber} /></Field>
        <Field label={t('patients.fields.email')} error={err('email')}><Input type="email" {...register('email')} invalid={!!errors.email} /></Field>
        <Field label={t('patients.fields.address')} className="lg:col-span-2"><Input {...register('address')} /></Field>
        <Field label={t('patients.fields.city')}><Input {...register('city')} /></Field>

        {section(t('patients.sections.medical'))}
        <Field label={t('patients.fields.allergies')}><Input {...register('allergies')} /></Field>
        <Field label={t('patients.fields.conditions')}><Input {...register('medicalConditions')} /></Field>
        <Field label={t('patients.fields.medications')}><Input {...register('currentMedications')} /></Field>
        <Field label={t('patients.fields.medicalNotes')} className="col-span-full"><Textarea rows={2} {...register('medicalNotes')} /></Field>

        {section(t('patients.sections.insurance'))}
        <Field label={t('patients.fields.insuranceProvider')}><Input {...register('insuranceProvider')} /></Field>
        <Field label={t('patients.fields.policyNumber')}><Input {...register('insurancePolicyNumber')} /></Field>
        <Field label={t('patients.fields.coverage')} error={err('insuranceCoveragePercent')}><Input type="number" min={0} max={100} {...register('insuranceCoveragePercent')} /></Field>

        {section(t('patients.sections.emergency'))}
        <Field label={t('patients.fields.emergencyName')}><Input {...register('emergencyContactName')} /></Field>
        <Field label={t('patients.fields.emergencyPhone')}><Input className="ltr" {...register('emergencyContactPhone')} /></Field>
        <Field label={t('patients.fields.emergencyRelation')}><Input {...register('emergencyContactRelation')} /></Field>

        {section(t('patients.sections.other'))}
        <Field label={t('patients.fields.primaryDentist')}>
          <Select {...register('primaryDentistId')}>
            <option value="">—</option>
            {dentists.data?.map((d) => <option key={d.id} value={d.id}>{d.fullName}</option>)}
          </Select>
        </Field>
        <Field label={t('patients.fields.referral')}><Input {...register('referralSource')} /></Field>
        <Field label={t('patients.fields.status')}>
          <Select {...register('status')}>
            {(['Active', 'Inactive', 'Archived'] as const).map((s) => <option key={s} value={s}>{t(`enum.PatientStatus.${s}`)}</option>)}
          </Select>
        </Field>
      </form>
    </Dialog>
  )
}
