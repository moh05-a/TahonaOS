import { useState, type FormEvent } from 'react'
import { Link, Navigate, useNavigate, useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { useAuth } from '@/auth/AuthProvider'
import { api, ApiError, errorMessage } from '@/lib/api'
import { applyLanguage } from '@/i18n'
import type { InvitationInfo, LoginResponse } from '@/lib/types'
import { Button } from '@/components/ui/Button'
import { Field, Input, LoadingBlock, Select, Switch } from '@/components/ui/primitives'
import { AuthCard } from './AuthCard'

const strong = (p: string) => p.length >= 8 && /[A-Z]/.test(p) && /[a-z]/.test(p) && /[0-9]/.test(p)

/** Sign-up from an operator's invitation: the clinic owner names the clinic and chooses a password. */
export default function WelcomePage() {
  const { t, i18n } = useTranslation()
  const { adopt, status } = useAuth()
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const token = params.get('token') ?? ''

  const invitation = useQuery({
    queryKey: ['invitation', token],
    queryFn: () => api.get<InvitationInfo>('/auth/invitation', { token }),
    enabled: token.length > 0,
    retry: false,
  })

  const [form, setForm] = useState<{ clinicName?: string; clinicNameAr: string; fullName?: string; phone: string; password: string; confirm: string; isDentist: boolean; language: string }>({
    clinicNameAr: '', phone: '', password: '', confirm: '', isDentist: true, language: i18n.language === 'en' ? 'en' : 'ar',
  })
  const [submitted, setSubmitted] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})

  if (status === 'authenticated' && !saving) return <Navigate to="/onboarding" replace />

  if (!token || invitation.isError) {
    return (
      <AuthCard title={t('auth.invalidInvitation')} subtitle={token ? t('auth.invalidInvitationBody') : t('auth.missingToken')}>
        <Link to="/login" className="text-sm font-medium text-primary-ink hover:underline">{t('auth.backToSignIn')}</Link>
      </AuthCard>
    )
  }
  if (!invitation.data) return <AuthCard title={t('auth.welcomeTitle')}><LoadingBlock rows={5} /></AuthCard>

  const info = invitation.data
  const clinicName = form.clinicName ?? info.clinicName ?? ''
  const fullName = form.fullName ?? info.contactName ?? ''
  const problems = {
    clinicName: !clinicName.trim(), fullName: !fullName.trim(), password: !strong(form.password), confirm: form.password !== form.confirm,
  }

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setSubmitted(true)
    setError(null)
    setFieldErrors({})
    if (Object.values(problems).some(Boolean)) return
    setSaving(true)
    try {
      const session = await api.post<LoginResponse>('/auth/accept-invitation', {
        token, clinicName: clinicName.trim(), clinicNameAr: form.clinicNameAr.trim() || null, fullName: fullName.trim(), password: form.password,
        language: form.language, phone: form.phone.trim() || null, isDentist: form.isDentist,
      })
      applyLanguage(form.language === 'en' ? 'en' : 'ar')
      adopt(session)
      navigate('/onboarding', { replace: true })
    } catch (err) {
      if (err instanceof ApiError) setFieldErrors(err.fieldErrors)
      setError(errorMessage(err))
      setSaving(false)
    }
  }

  const serverError = (name: string) => fieldErrors[name]?.[0] ?? fieldErrors[name[0].toUpperCase() + name.slice(1)]?.[0]

  return (
    <AuthCard title={t('auth.welcomeTitle')} subtitle={t('auth.welcomeBody', { days: info.trialDays })}>
      <form onSubmit={submit} className="space-y-4" noValidate>
        <p className="rounded-lg bg-surface-2 px-3 py-2 text-sm text-ink-2">{t('auth.invitedAs')} <span className="ltr font-medium text-ink">{info.email}</span></p>
        {error && <div role="alert" className="rounded-lg bg-critical-soft px-3 py-2 text-sm text-critical-ink">{error}</div>}

        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t('auth.clinicName')} required error={(submitted && problems.clinicName && t('common.required')) || serverError('clinicName')}>
            <Input value={clinicName} onChange={(e) => setForm({ ...form, clinicName: e.target.value })} maxLength={200} autoFocus />
          </Field>
          <Field label={t('auth.clinicNameAr')} hint={t('common.optional')}>
            <Input dir="rtl" value={form.clinicNameAr} onChange={(e) => setForm({ ...form, clinicNameAr: e.target.value })} maxLength={200} />
          </Field>
          <Field label={t('auth.yourName')} required error={(submitted && problems.fullName && t('common.required')) || serverError('fullName')}>
            <Input value={fullName} onChange={(e) => setForm({ ...form, fullName: e.target.value })} maxLength={150} autoComplete="name" />
          </Field>
          <Field label={t('auth.phone')} hint={t('common.optional')}>
            <Input className="ltr" inputMode="tel" value={form.phone} onChange={(e) => setForm({ ...form, phone: e.target.value })} maxLength={30} />
          </Field>
          <Field label={t('auth.choosePassword')} required hint={t('auth.passwordRule')} error={(submitted && problems.password && t('auth.passwordRule')) || serverError('password')}>
            <Input type="password" autoComplete="new-password" value={form.password} onChange={(e) => setForm({ ...form, password: e.target.value })} />
          </Field>
          <Field label={t('auth.confirmPassword')} required error={submitted && problems.confirm ? t('auth.passwordMismatch') : undefined}>
            <Input type="password" autoComplete="new-password" value={form.confirm} onChange={(e) => setForm({ ...form, confirm: e.target.value })} />
          </Field>
          <Field label={t('auth.language')}>
            <Select value={form.language} onChange={(e) => setForm({ ...form, language: e.target.value })}>
              <option value="ar">العربية</option><option value="en">English</option>
            </Select>
          </Field>
        </div>
        <Switch checked={form.isDentist} onChange={(v) => setForm({ ...form, isDentist: v })} label={t('auth.iAmDentist')} />
        <Button type="submit" size="lg" className="w-full" loading={saving}>{t('auth.createAccount')}</Button>
      </form>
    </AuthCard>
  )
}
