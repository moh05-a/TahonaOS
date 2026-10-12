import { forwardRef, useId, type HTMLAttributes, type InputHTMLAttributes, type ReactNode, type SelectHTMLAttributes, type TextareaHTMLAttributes } from 'react'
import { AlertTriangle, ChevronLeft, ChevronRight, Inbox, RotateCw } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { cn } from '@/lib/utils'
import { initials } from '@/lib/format'
import { errorMessage } from '@/lib/api'
import { Button } from './Button'

// ------------------------------------------------------------------ layout

export function Card({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return <div className={cn('rounded-2xl border border-line bg-surface shadow-card', className)} {...props} />
}

export function CardHeader({ title, subtitle, actions, className }: { title: ReactNode; subtitle?: ReactNode; actions?: ReactNode; className?: string }) {
  return (
    <div className={cn('flex items-start justify-between gap-3 px-5 pt-4 pb-3', className)}>
      <div className="min-w-0">
        <h3 className="text-sm font-semibold text-ink">{title}</h3>
        {subtitle && <p className="mt-0.5 text-xs text-ink-3">{subtitle}</p>}
      </div>
      {actions && <div className="flex shrink-0 items-center gap-2">{actions}</div>}
    </div>
  )
}

export function PageHeader({ title, subtitle, actions, icon }: { title: ReactNode; subtitle?: ReactNode; actions?: ReactNode; icon?: ReactNode }) {
  return (
    <div className="mb-6 flex flex-wrap items-end justify-between gap-4">
      <div className="flex min-w-0 items-center gap-3">
        {icon}
        <div className="min-w-0">
          <h1 className="text-xl font-semibold tracking-tight text-ink sm:text-2xl">{title}</h1>
          {subtitle && <p className="mt-1 text-sm text-ink-2">{subtitle}</p>}
        </div>
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
    </div>
  )
}

// ------------------------------------------------------------------ badges

export type Tone = 'neutral' | 'primary' | 'good' | 'warning' | 'serious' | 'critical' | 'recovery' | 'info'

const tones: Record<Tone, string> = {
  neutral: 'bg-surface-3 text-ink-2',
  primary: 'bg-primary-soft text-primary-ink',
  good: 'bg-good-soft text-good-ink',
  warning: 'bg-warning-soft text-warning-ink',
  serious: 'bg-serious-soft text-serious-ink',
  critical: 'bg-critical-soft text-critical-ink',
  recovery: 'bg-recovery-soft text-recovery-ink',
  info: 'bg-[color-mix(in_srgb,var(--series-1)_14%,transparent)] text-[var(--series-1)]',
}

export function Badge({ tone = 'neutral', className, children, dot }: { tone?: Tone; className?: string; children: ReactNode; dot?: boolean }) {
  return (
    <span className={cn('inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-[11px] font-medium whitespace-nowrap', tones[tone], className)}>
      {dot && <span className="size-1.5 rounded-full bg-current opacity-80" />}
      {children}
    </span>
  )
}

// ------------------------------------------------------------------ form controls

const control =
  'w-full rounded-lg border border-line bg-surface px-3 text-sm text-ink placeholder:text-ink-3 transition-colors focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20 disabled:opacity-60'

export const Input = forwardRef<HTMLInputElement, InputHTMLAttributes<HTMLInputElement> & { invalid?: boolean }>(function Input(
  { className, invalid, ...props }, ref) {
  return <input ref={ref} className={cn(control, 'h-9', invalid && 'border-critical focus:border-critical focus:ring-critical/20', className)} {...props} />
})

export const Textarea = forwardRef<HTMLTextAreaElement, TextareaHTMLAttributes<HTMLTextAreaElement> & { invalid?: boolean }>(function Textarea(
  { className, invalid, ...props }, ref) {
  return <textarea ref={ref} className={cn(control, 'min-h-20 py-2', invalid && 'border-critical', className)} {...props} />
})

export const Select = forwardRef<HTMLSelectElement, SelectHTMLAttributes<HTMLSelectElement> & { invalid?: boolean }>(function Select(
  { className, invalid, children, ...props }, ref) {
  return (
    <select ref={ref} className={cn(control, 'h-9 pe-8', invalid && 'border-critical', className)} {...props}>
      {children}
    </select>
  )
})

export function Field({ label, error, hint, children, className, required }: {
  label: ReactNode; error?: string; hint?: ReactNode; children: ReactNode; className?: string; required?: boolean
}) {
  return (
    <label className={cn('block space-y-1.5', className)}>
      <span className="text-xs font-medium text-ink-2">
        {label}
        {required && <span className="text-critical"> *</span>}
      </span>
      {children}
      {error ? <span className="block text-xs text-critical-ink">{error}</span> : hint ? <span className="block text-xs text-ink-3">{hint}</span> : null}
    </label>
  )
}

export function Switch({ checked, onChange, label, disabled }: { checked: boolean; onChange: (v: boolean) => void; label?: ReactNode; disabled?: boolean }) {
  const id = useId()
  return (
    <label htmlFor={id} className={cn('inline-flex cursor-pointer items-center gap-2.5 text-sm text-ink', disabled && 'cursor-not-allowed opacity-60')}>
      <button
        id={id}
        type="button"
        role="switch"
        aria-checked={checked}
        disabled={disabled}
        onClick={() => onChange(!checked)}
        className={cn('relative inline-flex h-5 w-9 shrink-0 rounded-full transition-colors', checked ? 'bg-primary' : 'bg-line-strong')}
      >
        <span className={cn('absolute top-0.5 size-4 rounded-full bg-white shadow transition-all', checked ? 'start-[18px]' : 'start-0.5')} />
      </button>
      {label}
    </label>
  )
}

// ------------------------------------------------------------------ feedback

export function Spinner({ className }: { className?: string }) {
  return <span className={cn('inline-block size-5 animate-spin rounded-full border-2 border-line border-t-primary', className)} />
}

export function Skeleton({ className, style }: { className?: string; style?: React.CSSProperties }) {
  return <div className={cn('animate-pulse rounded-lg bg-surface-3', className)} style={style} />
}

export function LoadingBlock({ rows = 4 }: { rows?: number }) {
  return (
    <div className="space-y-3 p-5">
      {Array.from({ length: rows }).map((_, i) => <Skeleton key={i} className="h-10" />)}
    </div>
  )
}

export function EmptyState({ icon, title, body, action, className }: { icon?: ReactNode; title: ReactNode; body?: ReactNode; action?: ReactNode; className?: string }) {
  return (
    <div className={cn('flex flex-col items-center justify-center px-6 py-12 text-center', className)}>
      <div className="mb-3 grid size-12 place-items-center rounded-2xl bg-surface-3 text-ink-3">{icon ?? <Inbox className="size-6" />}</div>
      <p className="text-sm font-semibold text-ink">{title}</p>
      {body && <p className="mt-1 max-w-sm text-sm text-ink-3">{body}</p>}
      {action && <div className="mt-4">{action}</div>}
    </div>
  )
}

export function ErrorState({ error, onRetry, className }: { error: unknown; onRetry?: () => void; className?: string }) {
  const { t } = useTranslation()
  return (
    <div className={cn('flex flex-col items-center justify-center px-6 py-10 text-center', className)} role="alert">
      <div className="mb-3 grid size-12 place-items-center rounded-2xl bg-critical-soft text-critical-ink">
        <AlertTriangle className="size-6" />
      </div>
      <p className="text-sm font-semibold text-ink">{t('common.errorTitle')}</p>
      <p className="mt-1 max-w-sm text-sm text-ink-3">{errorMessage(error)}</p>
      {onRetry && (
        <Button variant="outline" size="sm" className="mt-4" icon={<RotateCw className="size-3.5" />} onClick={onRetry}>
          {t('common.retry')}
        </Button>
      )}
    </div>
  )
}

// ------------------------------------------------------------------ misc

export function Avatar({ name, color, size = 'md' }: { name: string; color?: string; size?: 'sm' | 'md' | 'lg' }) {
  const s = size === 'sm' ? 'size-7 text-[10px]' : size === 'lg' ? 'size-12 text-base' : 'size-9 text-xs'
  return (
    <span
      className={cn('inline-grid shrink-0 place-items-center rounded-full font-semibold', s, !color && 'bg-primary-soft text-primary-ink')}
      style={color ? { background: `color-mix(in srgb, ${color} 16%, transparent)`, color } : undefined}
      aria-hidden
    >
      {initials(name)}
    </span>
  )
}

export function Tabs<T extends string>({ value, onChange, items, className }: {
  value: T; onChange: (v: T) => void; items: { value: T; label: ReactNode; count?: number; hidden?: boolean }[]; className?: string
}) {
  return (
    <div role="tablist" className={cn('flex gap-1 overflow-x-auto border-b border-line', className)}>
      {items.filter((i) => !i.hidden).map((item) => (
        <button
          key={item.value}
          role="tab"
          type="button"
          aria-selected={value === item.value}
          onClick={() => onChange(item.value)}
          className={cn(
            '-mb-px flex items-center gap-1.5 border-b-2 px-3 py-2.5 text-sm font-medium whitespace-nowrap transition-colors',
            value === item.value ? 'border-primary text-ink' : 'border-transparent text-ink-3 hover:text-ink-2',
          )}
        >
          {item.label}
          {item.count != null && <span className="rounded-full bg-surface-3 px-1.5 text-[10px] text-ink-2">{item.count}</span>}
        </button>
      ))}
    </div>
  )
}

export function Segmented<T extends string>({ value, onChange, items }: { value: T; onChange: (v: T) => void; items: { value: T; label: ReactNode }[] }) {
  return (
    <div className="inline-flex rounded-lg bg-surface-3 p-0.5">
      {items.map((i) => (
        <button
          key={i.value}
          type="button"
          onClick={() => onChange(i.value)}
          className={cn('rounded-md px-3 py-1 text-xs font-medium transition-colors', value === i.value ? 'bg-surface text-ink shadow-sm' : 'text-ink-3 hover:text-ink-2')}
        >
          {i.label}
        </button>
      ))}
    </div>
  )
}

export function Pagination({ page, totalPages, onChange }: { page: number; totalPages: number; onChange: (p: number) => void }) {
  const { t } = useTranslation()
  if (totalPages <= 1) return null
  return (
    <div className="flex items-center justify-between border-t border-line px-5 py-3 text-xs text-ink-3">
      <span>{t('common.page', { page, pages: totalPages })}</span>
      <div className="flex gap-1">
        <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => onChange(page - 1)} aria-label={t('common.previous')}>
          <ChevronLeft className="size-3.5 rtl:rotate-180" />
        </Button>
        <Button variant="outline" size="sm" disabled={page >= totalPages} onClick={() => onChange(page + 1)} aria-label={t('common.next')}>
          <ChevronRight className="size-3.5 rtl:rotate-180" />
        </Button>
      </div>
    </div>
  )
}

export function StatCard({ label, value, hint, icon, tone = 'primary', onClick, className }: {
  label: ReactNode; value: ReactNode; hint?: ReactNode; icon?: ReactNode; tone?: Tone; onClick?: () => void; className?: string
}) {
  const Comp = onClick ? 'button' : 'div'
  return (
    <Comp
      onClick={onClick}
      className={cn('flex flex-col rounded-2xl border border-line bg-surface p-4 text-start shadow-card transition-colors', onClick && 'hover:border-line-strong', className)}
    >
      <div className="flex items-center justify-between gap-2">
        <span className="text-xs font-medium text-ink-3">{label}</span>
        {icon && <span className={cn('grid size-8 place-items-center rounded-lg', tones[tone])}>{icon}</span>}
      </div>
      <span className="tabular mt-2 text-2xl font-semibold tracking-tight text-ink">{value}</span>
      {hint && <span className="mt-1 text-xs text-ink-3">{hint}</span>}
    </Comp>
  )
}

export function KeyValue({ label, value }: { label: ReactNode; value: ReactNode }) {
  return (
    <div className="flex items-start justify-between gap-4 py-2 text-sm">
      <span className="text-ink-3">{label}</span>
      <span className="text-end font-medium text-ink">{value || '—'}</span>
    </div>
  )
}

export function ProgressBar({ value, tone = 'primary', className }: { value: number; tone?: 'primary' | 'good' | 'recovery'; className?: string }) {
  const color = tone === 'good' ? 'bg-good' : tone === 'recovery' ? 'bg-recovery-gradient' : 'bg-primary'
  return (
    <div className={cn('h-1.5 w-full overflow-hidden rounded-full bg-surface-3', className)}>
      <div className={cn('h-full rounded-full transition-all', color)} style={{ width: `${Math.max(0, Math.min(100, value))}%` }} />
    </div>
  )
}
