import { useQuery } from '@tanstack/react-query'
import { api } from './api'
import type { Dentist, MessageTemplate, Procedure } from './types'

/** Reference data used across many screens (cached for 5 minutes). */
export const useDentists = (includeInactive = false) =>
  useQuery({
    queryKey: ['dentists', includeInactive],
    queryFn: () => api.get<Dentist[]>('/dentists', { includeInactive }),
    staleTime: 5 * 60_000,
  })

export const useProcedures = () =>
  useQuery({ queryKey: ['procedures'], queryFn: () => api.get<Procedure[]>('/procedures'), staleTime: 5 * 60_000 })

export const useTemplates = (enabled = true) =>
  useQuery({ queryKey: ['templates'], queryFn: () => api.get<MessageTemplate[]>('/messages/templates'), staleTime: 60_000, enabled })
