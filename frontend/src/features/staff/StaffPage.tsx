import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { KeyRound, Lock, Pencil, Plus, UserPlus } from 'lucide-react'
import { api, errorMessage } from '@/lib/api'
import { money, relative } from '@/lib/format'
import { useDentists } from '@/lib/queries'
import type { Dentist, Role, StaffUser } from '@/lib/types'
import { useAuth } from '@/auth/AuthProvider'
import { Avatar, Badge, Card, ErrorState, Field, Input, LoadingBlock, PageHeader, Select, Switch, Tabs } from '@/components/ui/primitives'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Table, THead, Td, Th, Tr } from '@/components/ui/Table'

type Tab = 'users' | 'dentists' | 'roles'

export default function StaffPage() {
  const { t } = useTranslation()
  const [tab, setTab] = useState<Tab>('users')
  return (
    <div className="space-y-5">
      <PageHeader title={t('staff.title')} subtitle={t('staff.subtitle')} />
      <Tabs<Tab> value={tab} onChange={setTab} items={[
        { value: 'users', label: t('staff.users') }, { value: 'dentists', label: t('staff.dentists') }, { value: 'roles', label: t('staff.roles') },
      ]} />
      {tab === 'users' && <Users />}
      {tab === 'dentists' && <Dentists />}
      {tab === 'roles' && <Roles />}
    </div>
  )
}

function Users() {
  const { t } = useTranslation()
  const { user: me } = useAuth()
  const q = useQuery({ queryKey: ['staff'], queryFn: () => api.get<StaffUser[]>('/staff') })
  const roles = useQuery({ queryKey: ['roles'], queryFn: () => api.get<Role[]>('/staff/roles') })
  const [editing, setEditing] = useState<StaffUser | null | undefined>(undefined)
  const [resetting, setResetting] = useState<StaffUser | null>(null)

  if (q.error) return <ErrorState error={q.error} onRetry={() => void q.refetch()} />
  return (
    <Card>
      <div className="flex justify-end border-b border-line p-4"><Button icon={<UserPlus className="size-4" />} onClick={() => setEditing(null)}>{t('staff.newUser')}</Button></div>
      {q.isLoading ? <LoadingBlock /> : (
        <Table>
          <THead><tr><Th>{t('common.name')}</Th><Th>{t('staff.role')}</Th><Th>{t('staff.lastLogin')}</Th><Th>{t('common.status')}</Th><Th /></tr></THead>
          <tbody>
            {q.data?.map((u) => (
              <Tr key={u.id}>
                <Td>
                  <div className="flex items-center gap-3">
                    <Avatar name={u.fullName} />
                    <div><p className="font-medium text-ink">{u.fullName}{u.id === me?.id && <Badge className="ms-2">you</Badge>}</p><p className="text-xs text-ink-3">{u.email}</p></div>
                  </div>
                </Td>
                <Td><Badge tone="primary">{u.roleName}</Badge>{u.dentistId && <Badge className="ms-1.5">{t('common.dentist')}</Badge>}</Td>
                <Td className="text-ink-2">{u.lastLoginAt ? relative(u.lastLoginAt) : '—'}</Td>
                <Td><Badge tone={u.isActive ? 'good' : 'neutral'} dot>{u.isActive ? t('staff.active') : t('staff.inactive')}</Badge></Td>
                <Td className="text-end">
                  <div className="flex justify-end gap-1">
                    <Button size="sm" variant="ghost" icon={<KeyRound className="size-3.5" />} onClick={() => setResetting(u)}>{t('staff.resetPassword')}</Button>
                    <Button size="icon" variant="ghost" className="size-8" onClick={() => setEditing(u)} aria-label={t('common.edit')}><Pencil className="size-3.5" /></Button>
                  </div>
                </Td>
              </Tr>
            ))}
          </tbody>
        </Table>
      )}
      <UserDialog user={editing} roles={roles.data ?? []} onClose={() => setEditing(undefined)} />
      <ResetPasswordDialog user={resetting} onClose={() => setResetting(null)} />
    </Card>
  )
}

function UserDialog({ user, roles, onClose }: { user: StaffUser | null | undefined; roles: Role[]; onClose: () => void }) {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const [f, setF] = useState({ email: '', fullName: '', fullNameAr: '', phone: '', roleId: '', password: '', isActive: true, createDentist: false, specialty: '', color: '#0ea5e9' })
  useEffect(() => {
    if (user === undefined) return
    setF(user ? { email: user.email, fullName: user.fullName, fullNameAr: user.fullNameAr ?? '', phone: user.phone ?? '', roleId: user.roleId, password: '', isActive: user.isActive, createDentist: false, specialty: '', color: '#0ea5e9' }
      : { email: '', fullName: '', fullNameAr: '', phone: '', roleId: roles.find((r) => r.name === 'Receptionist')?.id ?? roles[0]?.id ?? '', password: '', isActive: true, createDentist: false, specialty: '', color: '#0ea5e9' })
  }, [user, roles])

  const save = useMutation({
    mutationFn: () => user
      ? api.put(`/staff/${user.id}`, { fullName: f.fullName, fullNameAr: f.fullNameAr || null, phone: f.phone || null, roleId: f.roleId, isActive: f.isActive })
      : api.post('/staff', { email: f.email, fullName: f.fullName, fullNameAr: f.fullNameAr || null, phone: f.phone || null, roleId: f.roleId, password: f.password,
        createDentistProfile: f.createDentist, specialty: f.specialty || null, dentistColor: f.createDentist ? f.color : null }),
    onSuccess: () => { toast.success(t('common.saved')); void qc.invalidateQueries({ queryKey: ['staff'] }); void qc.invalidateQueries({ queryKey: ['dentists'] }); onClose() },
    onError: (e) => toast.error(errorMessage(e)),
  })
  const strong = f.password.length >= 8 && /[A-Z]/.test(f.password) && /[a-z]/.test(f.password) && /\d/.test(f.password)

  return (
    <Dialog open={user !== undefined} onClose={onClose} title={user ? t('common.edit') : t('staff.newUser')}
      footer={<><Button variant="outline" onClick={onClose}>{t('common.cancel')}</Button>
        <Button loading={save.isPending} disabled={!f.fullName.trim() || !f.roleId || (!user && (!f.email.includes('@') || !strong))} onClick={() => save.mutate()}>{t('common.save')}</Button></>}>
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label={t('common.email')} required><Input type="email" value={f.email} disabled={!!user} onChange={(e) => setF({ ...f, email: e.target.value })} /></Field>
        <Field label={t('staff.role')} required>
          <Select value={f.roleId} onChange={(e) => setF({ ...f, roleId: e.target.value })}>{roles.map((r) => <option key={r.id} value={r.id}>{r.name}</option>)}</Select>
        </Field>
        <Field label={t('patients.fields.fullName')} required><Input value={f.fullName} onChange={(e) => setF({ ...f, fullName: e.target.value })} /></Field>
        <Field label={t('patients.fields.fullNameAr')}><Input dir="rtl" value={f.fullNameAr} onChange={(e) => setF({ ...f, fullNameAr: e.target.value })} /></Field>
        <Field label={t('common.phone')}><Input className="ltr" value={f.phone} onChange={(e) => setF({ ...f, phone: e.target.value })} /></Field>
        {user ? (
          <Field label={t('common.status')}><div className="pt-1.5"><Switch checked={f.isActive} onChange={(v) => setF({ ...f, isActive: v })} label={f.isActive ? t('staff.active') : t('staff.inactive')} /></div></Field>
        ) : (
          <Field label={t('staff.password')} required hint="8+ chars, upper & lower case, digit" error={f.password && !strong ? '✗' : undefined}>
            <Input type="password" autoComplete="new-password" value={f.password} onChange={(e) => setF({ ...f, password: e.target.value })} />
          </Field>
        )}
        {!user && (
          <div className="space-y-3 sm:col-span-2">
            <Switch checked={f.createDentist} onChange={(v) => setF({ ...f, createDentist: v })} label={t('staff.createDentist')} />
            {f.createDentist && (
              <div className="grid gap-4 sm:grid-cols-2">
                <Field label={t('staff.specialty')}><Input value={f.specialty} onChange={(e) => setF({ ...f, specialty: e.target.value })} /></Field>
                <Field label={t('staff.color')}><Input type="color" value={f.color} onChange={(e) => setF({ ...f, color: e.target.value })} className="h-9 p-1" /></Field>
              </div>
            )}
          </div>
        )}
      </div>
    </Dialog>
  )
}

function ResetPasswordDialog({ user, onClose }: { user: StaffUser | null; onClose: () => void }) {
  const { t } = useTranslation()
  const [password, setPassword] = useState('')
  useEffect(() => { setPassword('') }, [user])
  const save = useMutation({
    mutationFn: () => api.post(`/staff/${user!.id}/reset-password`, { newPassword: password }),
    onSuccess: () => { toast.success(t('common.saved')); onClose() },
    onError: (e) => toast.error(errorMessage(e)),
  })
  const strong = password.length >= 8 && /[A-Z]/.test(password) && /[a-z]/.test(password) && /\d/.test(password)
  return (
    <Dialog open={!!user} onClose={onClose} size="sm" title={`${t('staff.resetPassword')} · ${user?.fullName ?? ''}`}
      footer={<><Button variant="outline" onClick={onClose}>{t('common.cancel')}</Button><Button loading={save.isPending} disabled={!strong} onClick={() => save.mutate()}>{t('common.save')}</Button></>}>
      <Field label={t('staff.password')} hint="8+ chars, upper & lower case, digit"><Input type="password" autoComplete="new-password" value={password} onChange={(e) => setPassword(e.target.value)} /></Field>
    </Dialog>
  )
}

function Dentists() {
  const { t } = useTranslation()
  const q = useDentists(true)
  const [editing, setEditing] = useState<Dentist | null | undefined>(undefined)
  return (
    <Card>
      <div className="flex justify-end border-b border-line p-4"><Button icon={<Plus className="size-4" />} onClick={() => setEditing(null)}>{t('staff.newDentist')}</Button></div>
      {q.isLoading ? <LoadingBlock /> : (
        <Table>
          <THead><tr><Th>{t('common.dentist')}</Th><Th>{t('staff.specialty')}</Th><Th>{t('staff.license')}</Th><Th className="text-end">{t('staff.upcoming')}</Th><Th className="text-end">{t('staff.production')}</Th><Th>{t('common.status')}</Th><Th /></tr></THead>
          <tbody>
            {q.data?.map((d) => (
              <Tr key={d.id}>
                <Td><div className="flex items-center gap-3"><Avatar name={d.fullName} color={d.color} /><div><p className="font-medium text-ink">{d.fullName}</p><p className="text-xs text-ink-3">{d.email}</p></div></div></Td>
                <Td className="text-ink-2">{d.specialty ?? '—'}</Td>
                <Td className="text-ink-2">{d.licenseNumber ?? '—'}</Td>
                <Td className="tabular text-end">{d.upcomingAppointments}</Td>
                <Td className="tabular text-end">{money(d.productionThisMonth)}</Td>
                <Td><Badge tone={d.isActive ? 'good' : 'neutral'} dot>{d.isActive ? t('staff.active') : t('staff.inactive')}</Badge></Td>
                <Td className="text-end"><Button size="icon" variant="ghost" className="size-8" onClick={() => setEditing(d)} aria-label={t('common.edit')}><Pencil className="size-3.5" /></Button></Td>
              </Tr>
            ))}
          </tbody>
        </Table>
      )}
      <DentistDialog dentist={editing} onClose={() => setEditing(undefined)} />
    </Card>
  )
}

function DentistDialog({ dentist, onClose }: { dentist: Dentist | null | undefined; onClose: () => void }) {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const [f, setF] = useState({ fullName: '', fullNameAr: '', specialty: '', phone: '', email: '', licenseNumber: '', color: '#0ea5e9', isActive: true })
  useEffect(() => {
    if (dentist === undefined) return
    setF(dentist ? { fullName: dentist.fullName, fullNameAr: dentist.fullNameAr ?? '', specialty: dentist.specialty ?? '', phone: dentist.phone ?? '', email: dentist.email ?? '',
      licenseNumber: dentist.licenseNumber ?? '', color: dentist.color, isActive: dentist.isActive } : { fullName: '', fullNameAr: '', specialty: '', phone: '', email: '', licenseNumber: '', color: '#0ea5e9', isActive: true })
  }, [dentist])
  const save = useMutation({
    mutationFn: () => {
      const body = { ...f, fullNameAr: f.fullNameAr || null, specialty: f.specialty || null, phone: f.phone || null, email: f.email || null, licenseNumber: f.licenseNumber || null, userId: dentist?.userId ?? null }
      return dentist ? api.put(`/dentists/${dentist.id}`, body) : api.post('/dentists', body)
    },
    onSuccess: () => { toast.success(t('common.saved')); void qc.invalidateQueries({ queryKey: ['dentists'] }); onClose() },
    onError: (e) => toast.error(errorMessage(e)),
  })
  return (
    <Dialog open={dentist !== undefined} onClose={onClose} title={dentist ? t('common.edit') : t('staff.newDentist')}
      footer={<><Button variant="outline" onClick={onClose}>{t('common.cancel')}</Button><Button loading={save.isPending} disabled={!f.fullName.trim()} onClick={() => save.mutate()}>{t('common.save')}</Button></>}>
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label={t('patients.fields.fullName')} required><Input value={f.fullName} onChange={(e) => setF({ ...f, fullName: e.target.value })} /></Field>
        <Field label={t('patients.fields.fullNameAr')}><Input dir="rtl" value={f.fullNameAr} onChange={(e) => setF({ ...f, fullNameAr: e.target.value })} /></Field>
        <Field label={t('staff.specialty')}><Input value={f.specialty} onChange={(e) => setF({ ...f, specialty: e.target.value })} /></Field>
        <Field label={t('staff.license')}><Input value={f.licenseNumber} onChange={(e) => setF({ ...f, licenseNumber: e.target.value })} /></Field>
        <Field label={t('common.phone')}><Input className="ltr" value={f.phone} onChange={(e) => setF({ ...f, phone: e.target.value })} /></Field>
        <Field label={t('common.email')}><Input type="email" value={f.email} onChange={(e) => setF({ ...f, email: e.target.value })} /></Field>
        <Field label={t('staff.color')}><Input type="color" value={f.color} onChange={(e) => setF({ ...f, color: e.target.value })} className="h-9 p-1" /></Field>
        <Field label={t('common.status')}><div className="pt-1.5"><Switch checked={f.isActive} onChange={(v) => setF({ ...f, isActive: v })} label={f.isActive ? t('staff.active') : t('staff.inactive')} /></div></Field>
      </div>
    </Dialog>
  )
}

function Roles() {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const roles = useQuery({ queryKey: ['roles'], queryFn: () => api.get<Role[]>('/staff/roles') })
  const all = useQuery({ queryKey: ['permissions'], queryFn: () => api.get<string[]>('/staff/permissions'), staleTime: Infinity })
  const update = useMutation({
    mutationFn: (v: { role: Role; permissions: string[] }) => api.put(`/staff/roles/${v.role.id}`, { description: v.role.description, permissions: v.permissions }),
    onSuccess: () => { toast.success(t('common.saved')); void qc.invalidateQueries({ queryKey: ['roles'] }) },
    onError: (e) => toast.error(errorMessage(e)),
  })
  if (roles.isLoading || all.isLoading) return <LoadingBlock />
  if (roles.error) return <ErrorState error={roles.error} />
  const groups = Object.entries((all.data ?? []).reduce<Record<string, string[]>>((acc, p) => {
    const g = p.split('.')[0]
    ;(acc[g] ??= []).push(p)
    return acc
  }, {}))

  return (
    <Card>
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="border-b border-line bg-surface-2 text-xs text-ink-3">
            <tr>
              <th className="px-5 py-3 text-start font-medium">{t('staff.permissions')}</th>
              {roles.data?.map((r) => (
                <th key={r.id} className="px-4 py-3 text-center font-medium">
                  <span className="block text-ink">{r.name}</span><span className="block text-[10px]">{r.userCount} {t('staff.users').toLowerCase()}</span>
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {groups.map(([group, perms]) => perms.map((perm, i) => (
              <tr key={perm} className={`border-b border-line ${i === 0 ? 'border-t-2 border-t-line' : ''}`}>
                <td className="px-5 py-2"><span className="font-mono text-xs text-ink-2">{perm}</span>{i === 0 && <span className="ms-2 text-[10px] text-ink-3 uppercase">{group}</span>}</td>
                {roles.data?.map((r) => {
                  const has = r.permissions.includes(perm)
                  const locked = r.name === 'Admin'
                  return (
                    <td key={r.id} className="px-4 py-2 text-center">
                      {locked ? <Lock className="mx-auto size-3.5 text-ink-3" aria-label={t('staff.adminLocked')} /> : (
                        <input type="checkbox" checked={has} disabled={update.isPending} className="size-4 accent-[var(--primary)]"
                          aria-label={`${r.name} ${perm}`}
                          onChange={() => update.mutate({ role: r, permissions: has ? r.permissions.filter((p) => p !== perm) : [...r.permissions, perm] })} />
                      )}
                    </td>
                  )
                })}
              </tr>
            )))}
          </tbody>
        </table>
      </div>
      <p className="px-5 py-3 text-xs text-ink-3">{t('staff.adminLocked')}</p>
    </Card>
  )
}
