// Helpers de sesión JWT. Nuxt los auto-importa desde app/utils.

const ROLE_CLAIM =
  'http://schemas.microsoft.com/ws/2008/06/identity/claims/role'

export interface TokenPayload {
  exp?: number
  role?: string
  nickname?: string
  email?: string
  [key: string]: unknown
}

export function getAuthToken(): string | null {
  if (import.meta.server) return null

  return localStorage.getItem('token')
}

export function clearAuthToken() {
  if (import.meta.server) return

  localStorage.removeItem('token')
}

// El JWT viene en base64url ("-" y "_"); atob() solo entiende base64 normal.
export function decodeToken(token: string): TokenPayload | null {
  try {
    const part = token.split('.')[1]

    if (!part) return null

    const base64 = part.replace(/-/g, '+').replace(/_/g, '/')
    const padded = base64.padEnd(Math.ceil(base64.length / 4) * 4, '=')

    const bytes = Array.from(atob(padded), c => c.charCodeAt(0))
    const json = new TextDecoder().decode(new Uint8Array(bytes))

    return JSON.parse(json) as TokenPayload
  } catch {
    return null
  }
}

export function isAuthTokenValid(token: string | null): boolean {
  if (!token) return false

  const payload = decodeToken(token)

  if (!payload) return false

  if (payload.exp && payload.exp * 1000 <= Date.now()) return false

  return true
}

export function getAuthRole(
  token: string | null = getAuthToken(),
): string | null {
  if (!token) return null

  const payload = decodeToken(token)

  if (!payload) return null

  return (payload.role ?? payload[ROLE_CLAIM] ?? null) as string | null
}
