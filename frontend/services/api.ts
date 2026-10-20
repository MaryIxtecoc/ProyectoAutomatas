const API_URL = 'http://localhost:5234/api'

export async function apiFetch<T>(
  endpoint: string,
  options: RequestInit = {},
): Promise<T> {
  const token = localStorage.getItem('token')

  const headers = new Headers(options.headers)

  headers.set('Content-Type', 'application/json')

  if (token) {
    headers.set('Authorization', `Bearer ${token}`)
  }

  const response = await fetch(`${API_URL}${endpoint}`, {
    ...options,
    headers,
  })

  if (!response.ok) {
    let message = 'Ocurrió un error en la solicitud.'

    try {
      const error = await response.json()
      message = error.message ?? message
    } catch {
      // La respuesta no contenía JSON.
    }

    throw new Error(message)
  }

  return response.json()
}