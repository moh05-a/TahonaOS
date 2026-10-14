import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Languages, Stethoscope } from 'lucide-react'
import { applyLanguage } from '@/i18n'
import { Button } from '@/components/ui/Button'

/** Layout for the signed-out pages that are reached from a link (invitation, password reset). */
export function AuthCard({ title, subtitle, children }: { title: ReactNode; subtitle?: ReactNode; children: ReactNode }) {
  const { i18n } = useTranslation()
  return (
    <div className="flex min-h-full flex-col items-center bg-surface-2 px-4 py-10">
      <div className="flex w-full max-w-lg items-center justify-between">
        <div className="flex items-center gap-2.5">
          <span className="grid size-9 place-items-center rounded-xl bg-primary text-white shadow-sm"><Stethoscope className="size-5" /></span>
          <span className="text-base font-semibold text-ink">Tahona</span>
        </div>
        <Button variant="ghost" size="sm" icon={<Languages className="size-4" />} onClick={() => applyLanguage(i18n.language === 'ar' ? 'en' : 'ar')}>
          {i18n.language === 'ar' ? 'English' : 'العربية'}
        </Button>
      </div>
      <div className="mt-8 w-full max-w-lg rounded-2xl border border-line bg-surface p-6 shadow-card sm:p-8">
        <h1 className="text-xl font-semibold text-ink">{title}</h1>
        {subtitle && <p className="mt-1.5 text-sm text-ink-2">{subtitle}</p>}
        <div className="mt-6">{children}</div>
      </div>
    </div>
  )
}
