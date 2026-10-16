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

  if (getAuthRole(token) !== 'ADMIN') {
    return navigateTo('/dashboard')
  }
})
