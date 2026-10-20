export default defineNuxtRouteMiddleware(() => {
  if (import.meta.server) return

  if (!isAuthTokenValid(getAuthToken())) {
    clearAuthToken()

    return navigateTo('/')
  }
})
