export default defineNuxtRouteMiddleware(() => {
  if (import.meta.server) return

  const token = getAuthToken()

  if (!isAuthTokenValid(token)) {
    clearAuthToken()

    return navigateTo('/')
  }

  const role = getAuthRole(token)

  if (role !== 'ADMIN' && role !== 'SUPERVISOR') {
    return navigateTo('/dashboard')
  }
})
