declare const defineNuxtRouteMiddleware: any
declare const navigateTo: any

import { getAuthToken, isAuthTokenValid, clearAuthToken, getAuthRole } from '../services/api'

export default defineNuxtRouteMiddleware(() => {
  if (typeof window === 'undefined') return

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
