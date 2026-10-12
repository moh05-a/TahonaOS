import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { api, refreshSession, registerSessionListener, setAccessToken } from '@/lib/api'
import { setFormatContext } from '@/lib/format'
import { applyLanguage, type Language } from '@/i18n'
import type { LoginResponse, UserProfile } from '@/lib/types'
import type { Permission } from '@/lib/utils'

interface AuthState {
  user: UserProfile | null
  status: 'loading' | 'authenticated' | 'anonymous'
  login: (email: string, password: string) => Promise<void>
  logout: () => Promise<void>
  can: (permission: Permission) => boolean
}

const AuthContext = createContext<AuthState | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserProfile | null>(null)
  const [status, setStatus] = useState<AuthState['status']>('loading')
  const queryClient = useQueryClient()

  const applySession = useCallback((session: LoginResponse | null) => {
    if (session) {
      setAccessToken(session.accessToken)
      setUser(session.user)
      setFormatContext(session.user.clinic.currency, session.user.clinic.timeZone)
      setStatus('authenticated')
    } else {
      setAccessToken(null)
      setUser(null)
      setStatus('anonymous')
      queryClient.clear()
    }
  }, [queryClient])

  // Restore the session from the HttpOnly refresh cookie on load.
  useEffect(() => {
    registerSessionListener(applySession)
    void refreshSession().then(applySession)
  }, [applySession])

  // Proactively refresh the short-lived access token every 12 minutes while the tab is open.
  useEffect(() => {
    if (status !== 'authenticated') return
    const id = window.setInterval(() => void refreshSession().then((s) => s && applySession(s)), 12 * 60_000)
    return () => window.clearInterval(id)
  }, [status, applySession])

  const login = useCallback(async (email: string, password: string) => {
    const session = await api.post<LoginResponse>('/auth/login', { email, password })
    applySession(session)
    const stored = localStorage.getItem('tahona.lang')
    if (!stored && (session.user.preferredLanguage === 'ar' || session.user.preferredLanguage === 'en'))
      applyLanguage(session.user.preferredLanguage as Language)
  }, [applySession])

  const logout = useCallback(async () => {
    try {
      await api.post('/auth/logout')
    } finally {
      applySession(null)
    }
  }, [applySession])

  const can = useCallback((permission: Permission) => !!user?.permissions.includes(permission), [user])

  const value = useMemo(() => ({ user, status, login, logout, can }), [user, status, login, logout, can])
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider')
  return ctx
}
