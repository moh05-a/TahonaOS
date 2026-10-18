import { useState } from 'react'
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useTranslation } from 'react-i18next'
import { CalendarCheck2, Languages, MessageCircle, Sparkles, TrendingUp } from 'lucide-react'
import { TahonaLogo } from '@/components/brand/TahonaLogo'
import { useAuth } from '@/auth/AuthProvider'
import { applyLanguage } from '@/i18n'
import { errorMessage } from '@/lib/api'
import { Button } from '@/components/ui/Button'
import { Field, Input } from '@/components/ui/primitives'

const schema = z.object({
  email: z.string().trim().email(),
  password: z.string().min(1),
})
type FormValues = z.infer<typeof schema>

/** Demo sign-in hints are for local development only; a production build never lists account names. */
const showDemo = import.meta.env.DEV

const demoAccounts = [
  { email: 'admin@tahona.demo', role: 'Admin' },
  { email: 'dentist@tahona.demo', role: 'Dentist' },
]

export default function LoginPage() {
  const { t, i18n } = useTranslation()
  const { status, login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [error, setError] = useState<string | null>(null)
  const { register, handleSubmit, setValue, formState: { errors, isSubmitting } } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { email: showDemo ? 'admin@tahona.demo' : '', password: '' },
  })

  if (status === 'authenticated') return <Navigate to="/" replace />

  const onSubmit = handleSubmit(async (values) => {
    setError(null)
    try {
      await login(values.email, values.password)
      const from = (location.state as { from?: string } | null)?.from
      navigate(from && from !== '/login' ? from : '/', { replace: true })
    } catch (e) {
      setError(errorMessage(e))
    }
  })

  return (
    <div className="grid min-h-full lg:grid-cols-2">
      <div className="relative hidden overflow-hidden bg-recovery-gradient p-12 text-white lg:flex lg:flex-col lg:justify-between">
        <TahonaLogo size={36} variant="white" showTagline lang={i18n.language === 'ar' ? 'ar' : 'en'} />
        <div className="max-w-md">
          <h1 className="text-3xl leading-tight font-semibold">{t('auth.heroTitle')}</h1>
          <p className="mt-4 text-white/80">{t('auth.heroBody')}</p>
          <ul className="mt-8 space-y-3 text-sm text-white/90">
            {[
              { icon: Sparkles, text: t('nav.recovery') },
              { icon: CalendarCheck2, text: t('nav.appointments') },
              { icon: MessageCircle, text: 'WhatsApp' },
              { icon: TrendingUp, text: t('nav.reports') },
            ].map(({ icon: Icon, text }) => (
              <li key={text} className="flex items-center gap-3"><Icon className="size-4" />{text}</li>
            ))}
          </ul>
        </div>
        <p className="text-xs text-white/60">Amman · JOD · Asia/Amman</p>
        <div className="pointer-events-none absolute -end-24 -bottom-24 size-96 rounded-full bg-white/10 blur-2xl" />
      </div>

      <div className="flex flex-col items-center justify-center p-6 sm:p-12">
        <div className="mb-8 flex w-full max-w-sm items-center justify-between lg:justify-end">
          {/* On a phone the coloured panel is hidden, so the logo sits above the form instead. */}
          <TahonaLogo size={30} lang={i18n.language === 'ar' ? 'ar' : 'en'} className="lg:hidden" />
          <Button variant="ghost" size="sm" icon={<Languages className="size-4" />} onClick={() => applyLanguage(i18n.language === 'ar' ? 'en' : 'ar')}>
            {i18n.language === 'ar' ? 'English' : 'العربية'}
          </Button>
        </div>
        <form onSubmit={onSubmit} className="w-full max-w-sm space-y-5" noValidate>
          <div>
            <h2 className="text-2xl font-semibold text-ink">{t('auth.title')}</h2>
            <p className="mt-1 text-sm text-ink-3">{t('auth.subtitle')}</p>
          </div>
          {error && <div role="alert" className="rounded-lg bg-critical-soft px-3 py-2 text-sm text-critical-ink">{error}</div>}
          <Field label={t('auth.email')} error={errors.email && t('common.required')}>
            <Input type="email" autoComplete="username" invalid={!!errors.email} {...register('email')} />
          </Field>
          <Field label={t('auth.password')} error={errors.password && t('common.required')}>
            <Input type="password" autoComplete="current-password" autoFocus invalid={!!errors.password} {...register('password')} />
          </Field>
          <Button type="submit" size="lg" className="w-full" loading={isSubmitting}>{t('auth.signIn')}</Button>
          <p className="text-center text-sm"><Link to="/forgot-password" className="font-medium text-primary-ink hover:underline">{t('auth.forgot')}</Link></p>

          {showDemo && <div className="rounded-xl border border-dashed border-line p-3">
            <p className="mb-2 text-xs font-medium text-ink-2">{t('auth.demoAccounts')}</p>
            <div className="grid grid-cols-2 gap-1.5">
              {demoAccounts.map((a) => (
                <button
                  key={a.email}
                  type="button"
                  onClick={() => setValue('email', a.email)}
                  className="rounded-lg bg-surface-2 px-2 py-1.5 text-start text-xs hover:bg-surface-3"
                >
                  <span className="block font-medium text-ink">{a.role}</span>
                  <span className="block truncate text-ink-3">{a.email}</span>
                </button>
              ))}
            </div>
            <p className="mt-2 text-[11px] text-ink-3">{t('auth.demoHint')}</p>
          </div>}
        </form>
      </div>
    </div>
  )
}
