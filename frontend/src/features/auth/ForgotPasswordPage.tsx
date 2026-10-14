import { useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { MailCheck } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { Button } from '@/components/ui/Button'
import { Field, Input, LoadingBlock } from '@/components/ui/primitives'
import { AuthCard } from './AuthCard'

/**
 * Asks for a reset link by e-mail where the deployment can send mail; otherwise explains who can reset the password.
 * The answer is the same whether or not the address has an account.
 */
export default function ForgotPasswordPage() {
  const { t } = useTranslation()
  const options = useQuery({ queryKey: ['auth-options'], queryFn: () => api.get<{ passwordResetByEmail: boolean }>('/auth/options'), staleTime: 300_000, retry: false })
  const [email, setEmail] = useState('')
  const [sending, setSending] = useState(false)
  const [sent, setSent] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const back = <Link to="/login" className="text-sm font-medium text-primary-ink hover:underline">{t('auth.backToSignIn')}</Link>

  if (options.isLoading) return <AuthCard title={t('auth.forgot')}><LoadingBlock rows={3} /></AuthCard>
  if (!options.data?.passwordResetByEmail) {
    return <AuthCard title={t('auth.forgot')} subtitle={t('auth.forgotBody')}>{back}</AuthCard>
  }
  if (sent) {
    return (
      <AuthCard title={t('auth.forgot')}>
        <p className="mb-4 flex items-start gap-2 text-sm text-ink-2"><MailCheck className="mt-0.5 size-4 shrink-0 text-good" />{t('auth.forgotSent')}</p>
        {back}
      </AuthCard>
    )
  }

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (!email.trim()) return
    setSending(true)
    setError(null)
    try {
      await api.post('/auth/forgot-password', { email: email.trim() })
      setSent(true)
    } catch (err) {
      setError(errorMessage(err))
    } finally {
      setSending(false)
    }
  }

  return (
    <AuthCard title={t('auth.forgot')} subtitle={t('auth.forgotEmailBody')}>
      <form onSubmit={submit} className="space-y-4" noValidate>
        {error && <div role="alert" className="rounded-lg bg-critical-soft px-3 py-2 text-sm text-critical-ink">{error}</div>}
        <Field label={t('auth.email')} required>
          <Input type="email" autoComplete="username" autoFocus value={email} onChange={(e) => setEmail(e.target.value)} />
        </Field>
        <Button type="submit" size="lg" className="w-full" loading={sending} disabled={!email.trim()}>{t('auth.sendResetLink')}</Button>
        <div>{back}</div>
      </form>
    </AuthCard>
  )
}
