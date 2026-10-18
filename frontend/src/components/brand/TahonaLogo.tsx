import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

// Tahona brand mark, concept C "Growth molar": an upper molar (roots up) that is also a rising bar chart.
// The tallest bar is violet, the colour of recovered revenue. Colours come from the theme tokens in
// index.css, so light and dark mode switch automatically. Static copies live in public/brand.

const CROWN = 'M16 62H84V78C84 84 80 88 74 88C68 88 66 84 60 84H40C34 84 32 88 26 88C20 88 16 84 16 78Z'

type MarkVariant = 'color' | 'mono' | 'white'

/** "color" is teal and violet; "mono" follows currentColor; "white" is for coloured backgrounds. */
export function TahonaMark({ size = 32, variant = 'color', className, title = 'Tahona' }: {
  size?: number; variant?: MarkVariant; className?: string; title?: string
}) {
  const body = variant === 'color' ? 'var(--primary)' : variant === 'white' ? '#fff' : 'currentColor'
  const accent = variant === 'color' ? 'var(--recovery-from)' : body
  return (
    <svg width={(size * 80) / 88} height={size} viewBox="10 6 80 88" role="img" aria-label={title} className={cn('shrink-0', className)}>
      <rect x="64" y="12" width="20" height="74" rx="10" fill={accent} />
      <rect x="16" y="34" width="20" height="52" rx="10" fill={body} />
      <rect x="40" y="50" width="20" height="36" rx="10" fill={body} />
      <path d={CROWN} fill={body} />
    </svg>
  )
}

/**
 * Mark and wordmark: "tahona", or "طاحونة" when the interface is Arabic. Flex order follows the document
 * direction, so in Arabic the mark sits on the right. A subtitle (such as the clinic's name) replaces the tagline.
 */
export function TahonaLogo({ size = 32, lang = 'en', variant = 'color', showTagline = false, subtitle, className }: {
  size?: number; lang?: 'en' | 'ar'; variant?: MarkVariant; showTagline?: boolean; subtitle?: ReactNode; className?: string
}) {
  const onColour = variant === 'white'
  return (
    <span className={cn('inline-flex min-w-0 items-center gap-2.5', className)}>
      <TahonaMark size={size} variant={variant} />
      <span className="flex min-w-0 flex-col leading-none">
        <span className={cn('font-semibold', onColour ? 'text-white' : 'text-ink')}
          style={{ fontSize: size * 0.78, letterSpacing: lang === 'en' ? '-0.035em' : undefined }}>
          {lang === 'ar' ? 'طاحونة' : 'tahona'}
        </span>
        {subtitle ? (
          <span className={cn('mt-1 truncate text-[11px] leading-tight', onColour ? 'text-white/80' : 'text-ink-3')}>{subtitle}</span>
        ) : showTagline && (
          <span className={cn('mt-1 uppercase tracking-[0.18em]', onColour ? 'text-white/80' : 'text-ink-3')} style={{ fontSize: size * 0.24 }}>
            {lang === 'ar' ? 'نظام عيادات الأسنان' : 'Dental Clinic OS'}
          </span>
        )}
      </span>
    </span>
  )
}
