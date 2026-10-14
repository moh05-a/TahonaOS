import { useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { CheckCircle2 } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { Button } from '@/components/ui/Button'
import { Field, Input } from '@/components/ui/primitives'
import { AuthCard } from './AuthCard'

const strong = (p: string) => p.length >= 8 && /[A-Z]/.test(p) && /[a-z]/.test(p) && /[0-9]/.test(p)

/** Sets a new password from a single-use reset link. */
export default function ResetPasswordPage() {
  const { t } = useTranslation()
  const [params] = useSearchParams()
  const token = params.get('token') ?? ''
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [submitted, setSubmitted] = useState(false)
  const [saving, setSaving] = useState(false)
  const [done, setDone] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const signIn = <Link to="/login" className="text-sm font-medium text-primary-ink hover:underline">{t('auth.backToSignIn')}</Link>

  if (!token) return <AuthCard title={t('auth.resetTitle')} subtitle={t('auth.missingToken')}>{signIn}</AuthCard>
  if (done) {
    return (
      <AuthCard title={t('auth.resetTitle')}>
        <p className="mb-4 flex items-start gap-2 text-sm text-ink-2"><CheckCircle2 className="mt-0.5 size-4 shrink-0 text-good" />{t('auth.resetDone')}</p>
        {signIn}
      </AuthCard>
    )
  }

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setSubmitted(true)
    setError(null)
    if (!strong(password) || password !== confirm) return
    setSaving(true)
    try {
      await api.post('/auth/reset-password', { token, newPassword: password })
      setDone(true)
    } catch (err) {
      setError(errorMessage(err))
    } finally {
      setSaving(false)
    }
  }

  return (
    <AuthCard title={t('auth.resetTitle')} subtitle={t('auth.resetBody')}>
      <form onSubmit={submit} className="space-y-4" noValidate>
        {error && <div role="alert" className="rounded-lg bg-critical-soft px-3 py-2 text-sm text-critical-ink">{error}</div>}
        <Field label={t('auth.choosePassword')} required hint={t('auth.passwordRule')} error={submitted && !strong(password) ? t('auth.passwordRule') : undefined}>
          <Input type="password" autoComplete="new-password" autoFocus value={password} onChange={(e) => setPassword(e.target.value)} />
        </Field>
        <Field label={t('auth.confirmPassword')} required error={submitted && password !== confirm ? t('auth.passwordMismatch') : undefined}>
          <Input type="password" autoComplete="new-password" value={confirm} onChange={(e) => setConfirm(e.target.value)} />
        </Field>
        <Button type="submit" size="lg" className="w-full" loading={saving}>{t('auth.setPassword')}</Button>
        <div>{signIn}</div>
      </form>
    </AuthCard>
  )
}
