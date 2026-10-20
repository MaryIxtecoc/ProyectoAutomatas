export default defineNuxtRouteMiddleware(() => {
  if (import.meta.server) return

  const token = getAuthToken()

  if (!isAuthTokenValid(token)) {
    clearAuthToken()

    return navigateTo('/')
  }

  if (getAuthRole(token) !== 'ADMIN') {
    return navigateTo('/dashboard')
  }
})
