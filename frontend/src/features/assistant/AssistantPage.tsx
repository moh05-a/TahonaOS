import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { useMutation, useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import Markdown from 'react-markdown'
import { Bot, Database, RotateCcw, Send, ShieldCheck, Sparkles, User } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import type { AssistantAnswer, AssistantStatus, DataTable } from '@/lib/types'
import { Badge, Card, PageHeader, Spinner, Textarea } from '@/components/ui/primitives'
import { Button } from '@/components/ui/Button'
import { Table, THead, Td, Th, Tr } from '@/components/ui/Table'

interface Turn { role: 'user' | 'assistant'; content: string; answer?: AssistantAnswer; error?: boolean }

/** Conversational analytics over clinic data. Every answer shows the exact rows it was based on. */
export default function AssistantPage() {
  const { t } = useTranslation()
  const [turns, setTurns] = useState<Turn[]>([])
  const [input, setInput] = useState('')
  const bottomRef = useRef<HTMLDivElement>(null)
  const status = useQuery({ queryKey: ['assistant-status'], queryFn: () => api.get<AssistantStatus>('/ai/assistant/status'), staleTime: 300_000 })

  const ask = useMutation({
    mutationFn: (question: string) => api.post<AssistantAnswer>('/ai/assistant', {
      question,
      history: turns.filter((x) => !x.error).slice(-10).map((x) => ({ role: x.role, content: x.content })),
    }),
    onSuccess: (answer) => setTurns((ts) => [...ts, { role: 'assistant', content: answer.answer, answer }]),
    onError: (e) => setTurns((ts) => [...ts, { role: 'assistant', content: errorMessage(e), error: true }]),
  })

  useEffect(() => { void bottomRef.current?.scrollIntoView({ behavior: 'smooth' }) }, [turns, ask.isPending])

  const submit = (q?: string) => {
    const question = (q ?? input).trim()
    if (!question || ask.isPending) return
    setTurns((ts) => [...ts, { role: 'user', content: question }])
    setInput('')
    ask.mutate(question)
  }

  return (
    <div className="mx-auto flex h-[calc(100vh-8rem)] max-w-4xl flex-col">
      <PageHeader
        icon={<span className="grid size-11 place-items-center rounded-2xl bg-recovery-gradient text-white"><Bot className="size-5" /></span>}
        title={t('assistant.title')}
        subtitle={t('assistant.subtitle')}
        actions={
          <>
            {status.data && (
              <Badge tone={status.data.llmConfigured ? 'recovery' : 'neutral'}>
                <Sparkles className="size-3" />{status.data.llmConfigured ? `${status.data.provider} · ${status.data.model}` : t('assistant.builtIn')}
              </Badge>
            )}
            {turns.length > 0 && <Button variant="outline" size="sm" icon={<RotateCcw className="size-3.5" />} onClick={() => setTurns([])}>{t('assistant.newChat')}</Button>}
          </>
        }
      />

      <Card className="flex min-h-0 flex-1 flex-col">
        <div className="flex-1 space-y-6 overflow-y-auto p-5">
          {turns.length === 0 && (
            <div className="py-6 text-center">
              <div className="mx-auto mb-4 grid size-14 place-items-center rounded-2xl bg-recovery-soft text-recovery-ink"><Sparkles className="size-6" /></div>
              <p className="text-sm font-medium text-ink">{t('assistant.suggestions')}</p>
              <div className="mx-auto mt-4 flex max-w-2xl flex-wrap justify-center gap-2">
                {(status.data?.suggestedQuestions ?? []).map((q) => (
                  <button key={q} onClick={() => submit(q)} className="rounded-full border border-line bg-surface-2 px-3 py-1.5 text-sm text-ink-2 transition-colors hover:border-recovery hover:text-recovery-ink">
                    {q}
                  </button>
                ))}
              </div>
              <p className="mx-auto mt-6 flex max-w-lg items-start justify-center gap-1.5 text-xs text-ink-3"><ShieldCheck className="mt-0.5 size-3.5 shrink-0" />{t('assistant.disclaimer')}</p>
            </div>
          )}

          {turns.map((turn, i) => (
            <div key={i} className={turn.role === 'user' ? 'flex justify-end' : 'flex gap-3'}>
              {turn.role === 'assistant' && <span className="grid size-8 shrink-0 place-items-center rounded-full bg-recovery-gradient text-white"><Bot className="size-4" /></span>}
              {turn.role === 'user' ? (
                <div className="flex max-w-[80%] items-start gap-2">
                  <div className="rounded-2xl rounded-se-sm bg-primary px-4 py-2.5 text-sm text-white" dir="auto">{turn.content}</div>
                  <span className="grid size-8 shrink-0 place-items-center rounded-full bg-surface-3 text-ink-2"><User className="size-4" /></span>
                </div>
              ) : (
                <div className="min-w-0 flex-1 space-y-3">
                  <div className={`rounded-2xl rounded-ss-sm px-4 py-3 text-sm ${turn.error ? 'bg-critical-soft text-critical-ink' : 'bg-surface-2 text-ink'}`}>
                    <div className="prose-sm space-y-2 [&_li]:ms-4 [&_ol]:list-decimal [&_strong]:font-semibold [&_ul]:list-disc" dir="auto"><Markdown>{turn.content}</Markdown></div>
                  </div>
                  {turn.answer && turn.answer.data.length > 0 && (
                    <div className="space-y-3">
                      <p className="flex items-center gap-1.5 text-xs font-medium text-ink-3"><Database className="size-3.5" />{t('assistant.dataUsed')}</p>
                      {turn.answer.data.map((d, j) => <DataCard key={j} table={d} />)}
                    </div>
                  )}
                  {turn.answer && (
                    <p className="text-[11px] text-ink-3">
                      {turn.answer.source === 'llm' ? (status.data?.provider ?? t('assistant.llm')) : t('assistant.builtIn')}
                      {turn.answer.toolsUsed.length > 0 && ` · ${t('assistant.toolsUsed')}: ${turn.answer.toolsUsed.join(', ')}`}
                    </p>
                  )}
                </div>
              )}
            </div>
          ))}
          {ask.isPending && (
            <div className="flex items-center gap-3 text-sm text-ink-3">
              <span className="grid size-8 place-items-center rounded-full bg-recovery-gradient text-white"><Bot className="size-4" /></span>
              <Spinner className="size-4" />{t('assistant.thinking')}
            </div>
          )}
          <div ref={bottomRef} />
        </div>

        <form className="flex items-end gap-2 border-t border-line p-3" onSubmit={(e) => { e.preventDefault(); submit() }}>
          <Textarea
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={(e) => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); submit() } }}
            placeholder={t('assistant.placeholder')}
            rows={1}
            maxLength={1000}
            className="max-h-32 min-h-10 resize-none"
            dir="auto"
          />
          <Button type="submit" size="icon" className="size-10" disabled={!input.trim() || ask.isPending} aria-label={t('common.send')}><Send className="size-4 rtl:-scale-x-100" /></Button>
        </form>
      </Card>
    </div>
  )
}

function DataCard({ table }: { table: DataTable }) {
  return (
    <div className="overflow-hidden rounded-xl border border-line">
      <p className="border-b border-line bg-surface-2 px-4 py-2 text-xs font-semibold text-ink-2">{table.title}</p>
      <div className="max-h-72 overflow-y-auto">
        <Table>
          <THead><tr>{table.columns.map((c) => <Th key={c}>{c}</Th>)}</tr></THead>
          <tbody>
            {table.rows.map((r, i) => (
              <Tr key={i}>
                {r.map((cell, j) => (
                  <Td key={j} className="whitespace-nowrap text-xs">
                    {j === 0 && table.rowPatientIds?.[i] ? <Link className="font-medium text-primary-ink hover:underline" to={`/patients/${table.rowPatientIds[i]}`}>{cell}</Link> : cell}
                  </Td>
                ))}
              </Tr>
            ))}
          </tbody>
        </Table>
      </div>
    </div>
  )
}
