declare const useRuntimeConfig: any

import { getAuthToken } from '../services/api'
// Helpers para leer archivos protegidos (fotos, credencial en PDF) desde el backend.
// No se pueden usar directamente como src de <img> porque requieren el header
// Authorization, así que se descargan como blob y se genera una object URL.
// Nuxt los auto-importa desde app/utils.

export async function fetchAuthedObjectUrl(endpoint: string): Promise<string | null> {
  if (typeof window === 'undefined') return null

  const apiUrl = useRuntimeConfig().public.apiUrl as string
  const token = getAuthToken()

  if (!token) return null

  try {
    const response = await fetch(`${apiUrl}${endpoint}`, {
      headers: { Authorization: `Bearer ${token}` }
    })

    if (!response.ok) return null

    const blob = await response.blob()
    return URL.createObjectURL(blob)
  } catch {
    return null
  }
}

export async function openAuthedFile(endpoint: string): Promise<boolean> {
  const url = await fetchAuthedObjectUrl(endpoint)

  if (!url) return false

  window.open(url, '_blank')
  return true
}
