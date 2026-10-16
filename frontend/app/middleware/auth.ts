declare const defineNuxtRouteMiddleware: any
declare const navigateTo: any

import { getAuthToken, isAuthTokenValid, clearAuthToken, getAuthRole } from '../services/api'


export default defineNuxtRouteMiddleware(() => {
  if (typeof window === 'undefined') return

  if (!isAuthTokenValid(getAuthToken())) {
    clearAuthToken()

    return navigateTo('/')
  }
})
