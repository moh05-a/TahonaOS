import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { CheckCircle2, Lock, MessageCircle, Plus, X } from 'lucide-react'
import { api, errorMessage, http } from '@/lib/api'
import { date, number } from '@/lib/format'
import type { Account, ClinicSettings, WhatsAppConnection } from '@/lib/types'
import { Button } from '@/components/ui/Button'
import { ConfirmDialog } from '@/components/ui/Dialog'
import { Badge, Card, CardHeader, ErrorState, Field, Input, KeyValue, LoadingBlock, type Tone } from '@/components/ui/primitives'

const statusTone: Record<Account['status'], Tone> = { Trial: 'info', Active: 'good', PastDue: 'warning', Cancelled: 'critical', Suspended: 'critical' }

/** The clinic's plan and subscription state, and how to pay. Payment is confirmed by TahonaOS support for now. */
export function AccountSection() {
  const { t } = useTranslation()
  const q = useQuery({ queryKey: ['account'], queryFn: () => api.get<Account>('/settings/account') })
  if (q.error) return <ErrorState error={q.error} onRetry={() => void q.refetch()} />
  if (!q.data) return <LoadingBlock />
  const a = q.data
  return (
    <Card>
      <CardHeader title={t('settings.account.title')} actions={<Badge tone={statusTone[a.status]}>{t(`settings.account.status.${a.status}`)}</Badge>} />
      <div className="space-y-4 px-5 pb-5">
        {a.readOnly && (
          <p role="alert" className="flex gap-2 rounded-lg bg-critical-soft px-3 py-2 text-sm text-critical-ink"><Lock className="mt-0.5 size-4 shrink-0" />{t('account.readOnlyBody')}</p>
        )}
        <div className="divide-y divide-line">
          <KeyValue label={t('settings.account.plan')} value={a.plan} />
          {a.status === 'Trial' && <KeyValue label={t('settings.account.trialEnds')} value={a.trialEndsAt ? date(a.trialEndsAt) : '—'} />}
          <KeyValue label={t('settings.account.paidUntil')} value={a.paidUntil ? date(a.paidUntil) : t('settings.account.notPaidYet')} />
          {a.statusReason && <KeyValue label={t('settings.account.note')} value={a.statusReason} />}
          <KeyValue label={t('settings.account.since')} value={date(a.createdAt)} />
          <KeyValue label={t('settings.account.usage')} value={t('settings.account.usageValue', { patients: number(a.patients), staff: a.activeStaff, dentists: a.dentists })} />
        </div>
        <div className="rounded-xl bg-surface-2 p-4 text-sm">
          <p className="font-medium text-ink">{t('settings.account.howToPay')}</p>
          <p className="mt-1 whitespace-pre-line text-ink-2" dir="auto">{a.paymentInstructions ?? t('settings.account.noInstructions')}</p>
          {a.supportContact && <p className="mt-2 text-ink-2">{t('settings.account.support')}: <span className="ltr font-medium text-ink">{a.supportContact}</span></p>}
        </div>
        <p className="text-xs text-ink-3">{t('settings.account.policy')}</p>
      </div>
    </Card>
  )
}

/** Connect the clinic's own WhatsApp Business number. The token is stored encrypted and never shown again. */
export function WhatsAppSection() {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const q = useQuery({ queryKey: ['whatsapp-connection'], queryFn: () => api.get<WhatsAppConnection>('/settings/whatsapp') })
  const [phoneNumberId, setPhoneNumberId] = useState('')
  const [accessToken, setAccessToken] = useState('')
  const [confirming, setConfirming] = useState(false)
  const done = (c: WhatsAppConnection) => {
    qc.setQueryData(['whatsapp-connection'], c)
    void qc.invalidateQueries({ queryKey: ['integrations'] })
    void qc.invalidateQueries({ queryKey: ['messaging-status'] })
  }
  const connect = useMutation({
    mutationFn: () => api.put<WhatsAppConnection>('/settings/whatsapp', { phoneNumberId: phoneNumberId.trim(), accessToken: accessToken.trim() }),
    onSuccess: (c) => { done(c); setAccessToken(''); setPhoneNumberId(''); toast.success(t('settings.whatsapp.connected')) },
    onError: (e) => toast.error(errorMessage(e)),
  })
  const disconnect = useMutation({
    mutationFn: () => http.delete<WhatsAppConnection>('/settings/whatsapp').then((r) => r.data),
    onSuccess: (c) => { done(c); setConfirming(false) },
    onError: (e) => toast.error(errorMessage(e)),
  })
  if (!q.data) return <LoadingBlock />
  const c = q.data

  return (
    <Card className="md:col-span-2">
      <CardHeader title={<span className="flex items-center gap-2"><MessageCircle className="size-4" />{t('settings.whatsapp.title')}</span>}
        actions={<Badge tone={c.mode === 'manual' ? 'warning' : 'good'}>{t(`settings.whatsapp.mode.${c.mode}`)}</Badge>} />
      <div className="space-y-4 px-5 pb-5">
        {c.mode === 'clinic' ? (
          <>
            <p className="flex items-center gap-2 text-sm text-ink"><CheckCircle2 className="size-4 text-good" />
              <span>{t('settings.whatsapp.sendingFrom')} <span className="ltr font-medium">{c.displayPhoneNumber ?? c.phoneNumberId}</span>{c.verifiedName && ` · ${c.verifiedName}`}</span>
            </p>
            <Button variant="outline" onClick={() => setConfirming(true)}>{t('settings.whatsapp.disconnect')}</Button>
          </>
        ) : (
          <>
            <p className="text-sm text-ink-2">{t(c.mode === 'shared' ? 'settings.whatsapp.sharedBody' : 'settings.whatsapp.manualBody')}</p>
            {c.canConnect ? (
              <div className="grid gap-4 sm:grid-cols-2">
                <Field label={t('settings.whatsapp.phoneNumberId')} hint={t('settings.whatsapp.phoneNumberIdHint')}>
                  <Input className="ltr" inputMode="numeric" value={phoneNumberId} onChange={(e) => setPhoneNumberId(e.target.value)} autoComplete="off" />
                </Field>
                <Field label={t('settings.whatsapp.token')} hint={t('settings.whatsapp.tokenHint')}>
                  <Input className="ltr" type="password" value={accessToken} onChange={(e) => setAccessToken(e.target.value)} autoComplete="off" />
                </Field>
                <div className="sm:col-span-2">
                  <Button disabled={!phoneNumberId.trim() || !accessToken.trim()} loading={connect.isPending} onClick={() => connect.mutate()}>{t('settings.whatsapp.connect')}</Button>
                </div>
              </div>
            ) : <p className="rounded-lg bg-surface-2 px-3 py-2 text-sm text-ink-2">{t('settings.whatsapp.unavailable')}</p>}
          </>
        )}
        <p className="text-xs text-ink-3">{t('settings.whatsapp.rules')}</p>
      </div>
      <ConfirmDialog open={confirming} onClose={() => setConfirming(false)} onConfirm={() => disconnect.mutate()} loading={disconnect.isPending} danger
        title={t('settings.whatsapp.disconnect')} body={t('settings.whatsapp.disconnectBody')} confirmLabel={t('settings.whatsapp.disconnect')} />
    </Card>
  )
}

/** Dates the clinic is closed. Edited as part of the general settings form. */
export function HolidaysEditor({ value, onChange }: { value: ClinicSettings['holidays']; onChange: (dates: string[]) => void }) {
  const { t } = useTranslation()
  const [draft, setDraft] = useState('')
  const dates = [...(value ?? [])].sort()
  return (
    <div>
      <p className="mb-1.5 text-xs font-medium text-ink-2">{t('settings.holidays')}</p>
      <div className="flex flex-wrap items-center gap-2">
        {dates.map((d) => (
          <span key={d} className="inline-flex items-center gap-1 rounded-full bg-surface-3 py-1 ps-3 pe-1 text-sm text-ink">
            {date(`${d}T12:00:00Z`)}
            <button type="button" aria-label={t('common.delete')} onClick={() => onChange(dates.filter((x) => x !== d))} className="grid size-5 place-items-center rounded-full text-ink-3 hover:bg-line hover:text-ink">
              <X className="size-3" />
            </button>
          </span>
        ))}
        <Input type="date" className="w-40" aria-label={t('settings.holidays')} value={draft} onChange={(e) => setDraft(e.target.value)} />
        <Button type="button" size="sm" variant="outline" icon={<Plus className="size-3.5" />} disabled={!draft || dates.includes(draft)}
          onClick={() => { onChange([...dates, draft]); setDraft('') }}>{t('common.add')}</Button>
      </div>
      <p className="mt-1.5 text-xs text-ink-3">{t('settings.holidaysHint')}</p>
    </div>
  )
}
