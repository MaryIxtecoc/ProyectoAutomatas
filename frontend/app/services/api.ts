declare const useRuntimeConfig: any
declare const navigateTo: any

export function getAuthToken(): string | null {
  if (typeof window === 'undefined') return null
  return localStorage.getItem('token')
}

export function isAuthTokenValid(token: any): boolean {
  return !!token
}

export function clearAuthToken(): void {
  if (typeof window !== 'undefined') {
    localStorage.removeItem('token')
  }
}

export function getAuthRole(token: any): string | null {
  if (typeof window === 'undefined' || !token) return null
  try {
    const payload = JSON.parse(atob(token.split('.')[1]))
    return payload.role || null
  } catch {
    return null
  }
}

export async function apiFetch<T>(
  endpoint: string,
  options: RequestInit = {},
): Promise<T> {
  const config = useRuntimeConfig()
  const apiUrl = (config.public.apiUrl as string) || ''

  const token = getAuthToken()

  const headers = new Headers(options.headers)

  if (!(options.body instanceof FormData)) {
    headers.set('Content-Type', 'application/json')
  }

  if (token) {
    headers.set('Authorization', `Bearer ${token}`)
  }

  const response = await fetch(`${apiUrl}${endpoint}`, {
    ...options,
    headers,
  })

  // Sesión vencida o token inválido en endpoints protegidos.
  if (response.status === 401 && token && !endpoint.startsWith('/Auth/')) {
    clearAuthToken()
    await navigateTo('/')

    throw new Error('Tu sesión terminó (expiró o fue cerrada). Inicia sesión nuevamente.')
  }

  if (!response.ok) {
    let message = response.status === 404
      ? 'El servidor no encontró este recurso (404). Si acabas de actualizar el sistema, reinicia el backend.'
      : `Ocurrió un error en la solicitud (${response.status}).`

    try {
      const error = await response.json()

      // Errores de validación de ASP.NET: { errors: { Campo: ['msg'] } }
      const validation = error.errors
        ? Object.values(error.errors as Record<string, string[]>)[0]?.[0]
        : undefined

      message =
        error.message ??
        validation ??
        error.title ??
        error.detail ??
        message
    } catch {
      // respuesta no JSON
    }

    throw new Error(message)
  }

  if (response.status === 204) {
    return undefined as T
  }

  return response.json()
}

// Cierra la sesión en el servidor (queda registrada en auditoría y el JWT
// deja de servir) y luego localmente. Si el backend no responde, igual se
// cierra en este navegador.
export async function logoutSession(): Promise<void> {
  try {
    if (getAuthToken()) {
      await apiFetch('/Auth/logout', { method: 'POST' })
    }
  } catch {
    // Sesión ya cerrada o backend caído: no impide salir.
  } finally {
    clearAuthToken()
    await navigateTo('/')
  }
}
