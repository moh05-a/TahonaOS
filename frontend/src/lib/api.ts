import axios, { AxiosError, type AxiosRequestConfig } from 'axios'
import type { LoginResponse } from './types'

/**
 * HTTP client. The access token lives only in memory; the refresh token is an HttpOnly cookie the browser sends
 * to /api/auth/refresh. A 401 triggers one shared refresh attempt and a replay of the original request.
 */
export const http = axios.create({ baseURL: '/api', withCredentials: true, timeout: 60_000 })

let accessToken: string | null = null
let refreshing: Promise<LoginResponse | null> | null = null
let onSessionChange: ((session: LoginResponse | null) => void) | null = null

export const setAccessToken = (token: string | null) => {
  accessToken = token
}
export const registerSessionListener = (fn: (session: LoginResponse | null) => void) => {
  onSessionChange = fn
}

export function refreshSession(): Promise<LoginResponse | null> {
  refreshing ??= axios
    .post<LoginResponse>('/api/auth/refresh', {}, { withCredentials: true })
    .then((r) => {
      setAccessToken(r.data.accessToken)
      return r.data
    })
    .catch(() => {
      setAccessToken(null)
      return null
    })
    .finally(() => {
      refreshing = null
    })
  return refreshing
}

http.interceptors.request.use((config) => {
  if (accessToken) config.headers.Authorization = `Bearer ${accessToken}`
  return config
})

http.interceptors.response.use(undefined, async (error: AxiosError) => {
  const original = error.config as (AxiosRequestConfig & { _retried?: boolean }) | undefined
  const isAuthCall = original?.url?.startsWith('/auth/')
  if (error.response?.status === 401 && original && !original._retried && !isAuthCall) {
    original._retried = true
    const session = await refreshSession()
    onSessionChange?.(session)
    if (session) {
      original.headers = { ...original.headers, Authorization: `Bearer ${session.accessToken}` }
      return http(original)
    }
  }
  return Promise.reject(toApiError(error))
})

// ------------------------------------------------------------------ errors

export class ApiError extends Error {
  status: number
  fieldErrors: Record<string, string[]>
  constructor(message: string, status: number, fieldErrors: Record<string, string[]> = {}) {
    super(message)
    this.status = status
    this.fieldErrors = fieldErrors
  }
}

function toApiError(error: AxiosError): ApiError {
  if (!error.response) {
    return new ApiError(error.code === 'ECONNABORTED' ? 'The server took too long to respond.' : 'Cannot reach the server. Check your connection.', 0)
  }
  const data = error.response.data as { title?: string; detail?: string; errors?: Record<string, string[]> } | undefined
  const status = error.response.status
  const fallback =
    status === 403 ? 'You do not have permission to do this.'
      : status === 404 ? 'Not found.'
        : status === 429 ? 'Too many requests. Please wait a moment.'
          : 'Something went wrong.'
  return new ApiError(data?.title ?? fallback, status, data?.errors ?? {})
}

export function errorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    const first = Object.values(error.fieldErrors)[0]?.[0]
    return first && error.status === 400 ? first : error.message
  }
  return error instanceof Error ? error.message : 'Something went wrong.'
}

// ------------------------------------------------------------------ helpers

export const api = {
  get: <T>(url: string, params?: object) => http.get<T>(url, { params: clean(params) }).then((r) => r.data),
  post: <T = void>(url: string, body?: unknown) => http.post<T>(url, body ?? {}).then((r) => r.data),
  put: <T = void>(url: string, body?: unknown) => http.put<T>(url, body).then((r) => r.data),
  patch: <T = void>(url: string, body?: unknown) => http.patch<T>(url, body).then((r) => r.data),
  delete: (url: string) => http.delete(url).then(() => undefined),
}

/** Drops empty values so they are not sent as `?x=` query parameters. */
function clean(params?: object) {
  if (!params) return undefined
  return Object.fromEntries(Object.entries(params).filter(([, v]) => v !== undefined && v !== null && v !== ''))
}
