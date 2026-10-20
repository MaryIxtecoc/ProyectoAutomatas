<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import { apiFetch } from '~/services/api'

// Panel de auditoría: exclusivo de ADMIN (el backend también lo exige).
definePageMeta({ middleware: 'admin' })

type Tab = 'resumen' | 'sesiones' | 'logins' | 'usuarios' | 'seguridad' | 'analisis' | 'sistema' | 'todo'

interface Summary {
  totalUsers: number
  activeUsers: number
  inactiveUsers: number
  onlineUsers: number
  openSessions: number
  logins24h: number
  failedLogins24h: number
  events24h: number
  loginsByMethod24h: { action: string, count: number }[]
}

interface UserActivity {
  id: string
  nickname: string
  email: string
  role: string
  isActive: boolean
  deleted: boolean
  lastLoginAt: string | null
  lastSeenAt: string | null
  openSessions: number
  online: boolean
}

interface SessionRow {
  id: string
  user: { id: string, nickname: string, email: string, role: string }
  loginMethod: string
  ip: string | null
  userAgent: string | null
  createdAt: string
  lastSeenAt: string
  expiresAt: string
  revokedAt: string | null
  revokeReason: string | null
  revokedBy: string | null
  status: 'ONLINE' | 'IDLE' | 'EXPIRED' | 'REVOKED'
}

const tabs: { key: Tab, label: string }[] = [
  { key: 'resumen', label: 'Resumen' },
  { key: 'sesiones', label: 'Usuarios y sesiones' },
  { key: 'logins', label: 'Inicios de sesión' },
  { key: 'usuarios', label: 'Cambios en usuarios' },
  { key: 'seguridad', label: 'Seguridad' },
  { key: 'analisis', label: 'Análisis' },
  { key: 'sistema', label: 'Sistema' },
  { key: 'todo', label: 'Bitácora completa' }
]

const activeTab = ref<Tab>('resumen')

const summary = ref<Summary | null>(null)
const users = ref<UserActivity[]>([])
const sessions = ref<SessionRow[]>([])
const currentSessionId = ref<string | null>(null)
const onlyOpenSessions = ref(true)
const userFilter = ref('')
const userStatusFilter = ref<'todos' | 'enlinea' | 'activos' | 'inactivos' | 'eliminados'>('todos')

const loading = ref(false)
const error = ref('')
const message = ref('')

// Historial filtrado por usuario (desde la tabla de usuarios).
const historyUser = ref<{ id: string, label: string } | null>(null)

let refreshTimer: ReturnType<typeof setInterval> | null = null

const methodLabels: Record<string, string> = {
  PASSWORD: 'Contraseña',
  FACE: 'Rostro',
  QR: 'QR',
  REGISTER: 'Registro',
  LOGIN: 'Contraseña',
  LOGIN_FACE: 'Rostro',
  LOGIN_QR: 'QR'
}

const statusLabels: Record<SessionRow['status'], string> = {
  ONLINE: 'En línea',
  IDLE: 'Inactiva',
  EXPIRED: 'Expirada',
  REVOKED: 'Cerrada'
}

const reasonLabels: Record<string, string> = {
  LOGOUT: 'El usuario cerró sesión',
  ADMIN: 'Cerrada por administrador',
  USER_DISABLED: 'Usuario desactivado',
  USER_DELETED: 'Usuario eliminado',
  ROLE_CHANGED: 'Cambio de rol',
  PASSWORD_CHANGED: 'Cambio de contraseña',
  PASSWORD_RESET: 'Contraseña restablecida'
}

const filteredUsers = computed(() => {
  const term = userFilter.value.trim().toLowerCase()

  return users.value.filter((u) => {
    if (term && !u.nickname.toLowerCase().includes(term) && !u.email.toLowerCase().includes(term)) {
      return false
    }

    switch (userStatusFilter.value) {
      case 'enlinea': return u.online
      case 'activos': return u.isActive && !u.deleted
      case 'inactivos': return !u.isActive && !u.deleted
      case 'eliminados': return u.deleted
      default: return true
    }
  })
})

function formatDate(value: string | null) {
  if (!value) return '—'
  return new Date(value).toLocaleString('es-GT', { dateStyle: 'short', timeStyle: 'short' })
}

function timeAgo(value: string | null) {
  if (!value) return 'Nunca'
  const seconds = Math.round((Date.now() - new Date(value).getTime()) / 1000)
  if (seconds < 60) return 'hace un momento'
  const minutes = Math.round(seconds / 60)
  if (minutes < 60) return `hace ${minutes} min`
  const hours = Math.round(minutes / 60)
  if (hours < 24) return `hace ${hours} h`
  return `hace ${Math.round(hours / 24)} d`
}

function shortAgent(ua: string | null) {
  if (!ua) return '—'
  const browser = /Edg\//.test(ua) ? 'Edge'
    : /Firefox\//.test(ua) ? 'Firefox'
    : /Chrome\//.test(ua) ? 'Chrome'
    : /Safari\//.test(ua) ? 'Safari'
    : 'Otro'
  const os = /Windows/.test(ua) ? 'Windows'
    : /Android/.test(ua) ? 'Android'
    : /iPhone|iPad/.test(ua) ? 'iOS'
    : /Mac OS/.test(ua) ? 'macOS'
    : /Linux/.test(ua) ? 'Linux'
    : ''
  return os ? `${browser} · ${os}` : browser
}

function flash(text: string) {
  message.value = text
  setTimeout(() => { if (message.value === text) message.value = '' }, 4000)
}

async function loadSummary() {
  summary.value = await apiFetch<Summary>('/Audit/summary')
}

async function loadUsers() {
  users.value = await apiFetch<UserActivity[]>('/Audit/users')
}

async function loadSessions() {
  const data = await apiFetch<{ currentSessionId: string | null, items: SessionRow[] }>(
    `/Audit/sessions?onlyOpen=${onlyOpenSessions.value}`
  )
  sessions.value = data.items
  currentSessionId.value = data.currentSessionId
}

async function refresh() {
  loading.value = true
  error.value = ''

  try {
    if (activeTab.value === 'resumen') {
      await loadSummary()
    } else if (activeTab.value === 'sesiones') {
      await Promise.all([loadUsers(), loadSessions()])
    }
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'No fue posible cargar la auditoría.'
  } finally {
    loading.value = false
  }
}

function selectTab(tab: Tab) {
  activeTab.value = tab
  refresh()
}

async function revokeSession(session: SessionRow) {
  const own = session.id === currentSessionId.value
  const text = own
    ? 'Esta es TU sesión actual. Si la cierras, saldrás del sistema. ¿Continuar?'
    : `¿Cerrar la sesión de ${session.user.nickname} (${methodLabels[session.loginMethod] ?? session.loginMethod}, ${session.ip ?? 'IP desconocida'})?`

  if (!confirm(text)) return

  try {
    const result = await apiFetch<{ message: string }>(`/Audit/sessions/${session.id}/revoke`, { method: 'POST' })
    flash(result.message)
    await refresh()
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'No fue posible cerrar la sesión.'
  }
}

async function revokeAllForUser(user: UserActivity) {
  if (!confirm(`¿Cerrar TODAS las sesiones abiertas de ${user.nickname}? Tendrá que volver a iniciar sesión.`)) return

  try {
    const result = await apiFetch<{ message: string }>(`/Audit/users/${user.id}/revoke-sessions`, { method: 'POST' })
    flash(result.message)
    await refresh()
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'No fue posible cerrar las sesiones.'
  }
}

function showHistory(user: UserActivity) {
  historyUser.value = { id: user.id, label: `${user.nickname} (${user.email})` }
  activeTab.value = 'todo'
}

onMounted(() => {
  refresh()

  // Usuarios y sesiones se actualizan solos cada 30 s mientras la pestaña está abierta.
  refreshTimer = setInterval(() => {
    if (activeTab.value === 'sesiones' || activeTab.value === 'resumen') {
      refresh()
    }
  }, 30000)
})

onBeforeUnmount(() => {
  if (refreshTimer) clearInterval(refreshTimer)
})
</script>

<template>
  <main class="audit-page">
    <header class="topbar">
      <div class="brand">
        <span class="brand-mark">L</span>
        <div>
          <h1>Léxica</h1>
          <p>Auditoría del sistema</p>
        </div>
      </div>

      <div class="topbar-actions">
        <NuxtLink to="/dashboard" class="link-button">Análisis</NuxtLink>
        <NuxtLink to="/supervisor" class="link-button">Supervisión</NuxtLink>
        <NuxtLink to="/users" class="link-button">Usuarios</NuxtLink>
        <NuxtLink to="/solicitudes" class="link-button">Solicitudes</NuxtLink>
        <NuxtLink to="/parametros" class="link-button">Parámetros</NuxtLink>
        <NuxtLink to="/perfil" class="link-button">Mi perfil</NuxtLink>
      </div>
    </header>

    <nav class="tabs">
      <button
        v-for="tab in tabs"
        :key="tab.key"
        type="button"
        :class="{ active: activeTab === tab.key }"
        @click="selectTab(tab.key)"
      >
        {{ tab.label }}
      </button>
    </nav>

    <div v-if="message" class="banner ok">{{ message }}</div>
    <div v-if="error" class="banner error">{{ error }}</div>

    <!-- RESUMEN -->
    <section v-if="activeTab === 'resumen'">
      <div v-if="loading && !summary" class="state">Cargando...</div>

      <template v-else-if="summary">
        <div class="cards">
          <div class="stat-card highlight">
            <span class="stat-value">{{ summary.onlineUsers }}</span>
            <span class="stat-label">Usuarios en línea (5 min)</span>
          </div>
          <div class="stat-card">
            <span class="stat-value">{{ summary.openSessions }}</span>
            <span class="stat-label">Sesiones abiertas</span>
          </div>
          <div class="stat-card">
            <span class="stat-value">{{ summary.logins24h }}</span>
            <span class="stat-label">Inicios de sesión (24 h)</span>
          </div>
          <div class="stat-card" :class="{ danger: summary.failedLogins24h > 0 }">
            <span class="stat-value">{{ summary.failedLogins24h }}</span>
            <span class="stat-label">Intentos fallidos (24 h)</span>
          </div>
          <div class="stat-card">
            <span class="stat-value">{{ summary.activeUsers }}</span>
            <span class="stat-label">Cuentas activas</span>
          </div>
          <div class="stat-card">
            <span class="stat-value">{{ summary.inactiveUsers }}</span>
            <span class="stat-label">Cuentas desactivadas</span>
          </div>
          <div class="stat-card">
            <span class="stat-value">{{ summary.events24h }}</span>
            <span class="stat-label">Eventos registrados (24 h)</span>
          </div>
        </div>

        <div class="panel">
          <h2>Inicios de sesión por método (24 h)</h2>
          <div v-for="m in summary.loginsByMethod24h" :key="m.action" class="bar-row">
            <span class="bar-label">{{ methodLabels[m.action] ?? m.action }}</span>
            <div class="bar-track">
              <div class="bar-fill" :style="{ width: (m.count / Math.max(1, summary.logins24h) * 100) + '%' }" />
            </div>
            <span class="bar-count">{{ m.count }}</span>
          </div>
          <p v-if="summary.loginsByMethod24h.length === 0" class="empty">Sin inicios de sesión en las últimas 24 horas.</p>
        </div>

        <div class="panel">
          <h2>Últimos intentos fallidos y acciones rechazadas</h2>
          <AuditLogTable show-category-filter initial-success="false" :page-size="10" />
        </div>
      </template>
    </section>

    <!-- USUARIOS Y SESIONES -->
    <section v-else-if="activeTab === 'sesiones'">
      <div class="panel">
        <div class="panel-head">
          <h2>Usuarios</h2>
          <div class="panel-tools">
            <input v-model="userFilter" type="search" placeholder="Buscar usuario">
            <select v-model="userStatusFilter">
              <option value="todos">Todos</option>
              <option value="enlinea">En línea ahora</option>
              <option value="activos">Cuentas activas</option>
              <option value="inactivos">Cuentas desactivadas</option>
              <option value="eliminados">Eliminados</option>
            </select>
            <button type="button" class="ghost" :disabled="loading" @click="refresh">↻ Actualizar</button>
          </div>
        </div>

        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Usuario</th>
                <th>Rol</th>
                <th>Conexión</th>
                <th>Cuenta</th>
                <th>Último inicio</th>
                <th>Última actividad</th>
                <th>Sesiones abiertas</th>
                <th>Acciones</th>
              </tr>
            </thead>
            <tbody>
              <tr v-if="filteredUsers.length === 0">
                <td colspan="8" class="state">{{ loading ? 'Cargando...' : 'No hay usuarios con este filtro.' }}</td>
              </tr>
              <tr v-for="u in filteredUsers" :key="u.id">
                <td>
                  <strong>{{ u.nickname }}</strong>
                  <span class="sub">{{ u.email }}</span>
                </td>
                <td>{{ u.role }}</td>
                <td>
                  <span v-if="u.online" class="badge ok">● En línea</span>
                  <span v-else-if="u.openSessions > 0" class="badge idle">Sesión inactiva</span>
                  <span v-else class="badge">Desconectado</span>
                </td>
                <td>
                  <span v-if="u.deleted" class="badge fail">Eliminada</span>
                  <span v-else-if="!u.isActive" class="badge warn">Desactivada</span>
                  <span v-else class="badge ok">Activa</span>
                </td>
                <td class="nowrap">{{ formatDate(u.lastLoginAt) }}</td>
                <td class="nowrap">{{ timeAgo(u.lastSeenAt) }}</td>
                <td class="center">{{ u.openSessions }}</td>
                <td class="actions">
                  <button type="button" class="link" @click="showHistory(u)">Historial</button>
                  <button
                    type="button"
                    class="danger"
                    :disabled="u.openSessions === 0"
                    @click="revokeAllForUser(u)"
                  >
                    Cerrar sesiones
                  </button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <div class="panel">
        <div class="panel-head">
          <h2>Sesiones</h2>
          <div class="panel-tools">
            <label class="check">
              <input v-model="onlyOpenSessions" type="checkbox" @change="refresh">
              Solo abiertas
            </label>
          </div>
        </div>
        <p class="hint">
          Si un usuario quedó con la sesión colgada o congelada, ciérrala aquí: su token deja de funcionar
          al instante y tendrá que volver a iniciar sesión.
        </p>

        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Usuario</th>
                <th>Método</th>
                <th>Estado</th>
                <th>IP</th>
                <th>Dispositivo</th>
                <th>Inicio</th>
                <th>Última actividad</th>
                <th>Expira / cierre</th>
                <th />
              </tr>
            </thead>
            <tbody>
              <tr v-if="sessions.length === 0">
                <td colspan="9" class="state">{{ loading ? 'Cargando...' : 'No hay sesiones.' }}</td>
              </tr>
              <tr v-for="s in sessions" :key="s.id" :class="{ own: s.id === currentSessionId }">
                <td>
                  <strong>{{ s.user.nickname }}</strong>
                  <span v-if="s.id === currentSessionId" class="own-tag">tu sesión</span>
                  <span class="sub">{{ s.user.email }}</span>
                </td>
                <td>{{ methodLabels[s.loginMethod] ?? s.loginMethod }}</td>
                <td>
                  <span
                    class="badge"
                    :class="{ ok: s.status === 'ONLINE', idle: s.status === 'IDLE', fail: s.status === 'REVOKED' }"
                  >
                    {{ statusLabels[s.status] }}
                  </span>
                </td>
                <td class="mono">{{ s.ip ?? '—' }}</td>
                <td :title="s.userAgent ?? ''">{{ shortAgent(s.userAgent) }}</td>
                <td class="nowrap">{{ formatDate(s.createdAt) }}</td>
                <td class="nowrap">{{ timeAgo(s.lastSeenAt) }}</td>
                <td class="nowrap">
                  <template v-if="s.revokedAt">
                    {{ formatDate(s.revokedAt) }}
                    <span class="sub">{{ reasonLabels[s.revokeReason ?? ''] ?? s.revokeReason }}{{ s.revokedBy ? ` · ${s.revokedBy}` : '' }}</span>
                  </template>
                  <template v-else>{{ formatDate(s.expiresAt) }}</template>
                </td>
                <td>
                  <button
                    v-if="s.status === 'ONLINE' || s.status === 'IDLE'"
                    type="button"
                    class="danger"
                    @click="revokeSession(s)"
                  >
                    Cerrar
                  </button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </section>

    <!-- BITÁCORAS POR CATEGORÍA -->
    <section v-else-if="activeTab === 'logins'" class="panel">
      <h2>Inicios y cierres de sesión</h2>
      <p class="hint">Quién entró, cuándo, desde qué IP y con qué método (contraseña, rostro o QR), incluidos los intentos fallidos y su motivo.</p>
      <AuditLogTable category="SESIONES" />
    </section>

    <section v-else-if="activeTab === 'usuarios'" class="panel">
      <h2>Cambios en usuarios</h2>
      <p class="hint">Altas, ediciones (campo por campo), cambios de rol, activaciones/desactivaciones y eliminaciones hechas por administradores.</p>
      <AuditLogTable category="USUARIOS" />
      <h2 class="spaced">Cambios de perfil hechos por los propios usuarios</h2>
      <AuditLogTable category="PERFIL" />
    </section>

    <section v-else-if="activeTab === 'seguridad'" class="panel">
      <h2>Seguridad</h2>
      <p class="hint">Contraseñas, restablecimientos, credenciales QR y registro biométrico. Nunca se guardan contraseñas, tokens, secretos de QR ni descriptores faciales.</p>
      <AuditLogTable category="SEGURIDAD" />
    </section>

    <section v-else-if="activeTab === 'analisis'" class="panel">
      <h2>Actividad de análisis</h2>
      <AuditLogTable category="ANALISIS" />
    </section>

    <section v-else-if="activeTab === 'sistema'" class="panel">
      <h2>Cambios en la parametrización</h2>
      <p class="hint">Altas y bajas en el diccionario de entidades del analizador léxico.</p>
      <AuditLogTable category="SISTEMA" />
    </section>

    <section v-else-if="activeTab === 'todo'" class="panel">
      <h2>Bitácora completa</h2>
      <AuditLogTable
        show-category-filter
        :user-id="historyUser?.id ?? null"
        :user-label="historyUser?.label ?? null"
        @clear-user="historyUser = null"
      />
    </section>
  </main>
</template>

<style scoped>
.audit-page {
  min-height: 100vh;
  background:
    linear-gradient(180deg, rgba(6, 12, 28, 0.55) 0%, rgba(6, 12, 28, 0.78) 100%),
    url('/images/apartado2.png') center / cover no-repeat fixed,
    #07101f;
  color: #eaf1fb;
  font-family: 'Poppins', 'Segoe UI', system-ui, sans-serif;
  padding: 24px;
}

.topbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 14px;
  margin-bottom: 20px;
}

.brand { display: flex; align-items: center; gap: 12px; }
.brand-mark {
  width: 38px; height: 38px; display: grid; place-items: center;
  border-radius: 11px;
  background: linear-gradient(135deg, rgba(73, 151, 208, 0.3), rgba(73, 151, 208, 0.05));
  border: 1px solid rgba(73, 151, 208, 0.35);
  color: #9cc6ea; font-weight: 800;
}
.brand h1 { margin: 0; font-size: 17px; }
.brand p { margin: 1px 0 0; font-size: 10px; color: #6682a7; }

.topbar-actions { display: flex; gap: 10px; flex-wrap: wrap; }
.link-button {
  padding: 9px 14px;
  border-radius: 9px;
  border: 1px solid rgba(73, 151, 208, 0.25);
  background: rgba(73, 151, 208, 0.06);
  color: #a9c5e3;
  text-decoration: none;
  font-size: 13px;
  font-weight: 600;
}

.tabs {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
  margin-bottom: 18px;
  border-bottom: 1px solid rgba(73, 151, 208, 0.15);
  padding-bottom: 10px;
}
.tabs button {
  background: transparent;
  color: #86a5c8;
  border: 1px solid transparent;
  padding: 8px 14px;
  border-radius: 9px;
}
.tabs button.active {
  background: rgba(73, 151, 208, 0.15);
  border-color: rgba(73, 151, 208, 0.35);
  color: #eaf1fb;
}

button {
  border: 0;
  border-radius: 9px;
  padding: 8px 12px;
  cursor: pointer;
  font: inherit;
  font-size: 12px;
  font-weight: 600;
}
button:disabled { opacity: 0.4; cursor: not-allowed; }
.ghost { background: rgba(73, 151, 208, 0.1); color: #cddcee; }
.link { background: none; color: #6faad8; padding: 4px 6px; }
.danger { background: rgba(248, 113, 113, 0.15); color: #fca5a5; }

.banner { padding: 10px 14px; border-radius: 10px; font-size: 12.5px; margin-bottom: 14px; }
.banner.ok { background: rgba(34, 197, 94, 0.12); color: #86efac; border: 1px solid rgba(34, 197, 94, 0.25); }
.banner.error { background: rgba(127, 29, 29, 0.15); color: #fca5a5; border: 1px solid rgba(248, 113, 113, 0.3); }

.state { text-align: center; padding: 24px; color: #86a5c8; }

.cards {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
  gap: 12px;
  margin-bottom: 16px;
}
.stat-card {
  padding: 18px;
  border-radius: 14px;
  background: rgba(10, 21, 40, 0.75);
  border: 1px solid rgba(73, 151, 208, 0.18);
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.stat-card.highlight { border-color: rgba(34, 197, 94, 0.4); }
.stat-card.danger { border-color: rgba(248, 113, 113, 0.45); }
.stat-value { font-size: 26px; font-weight: 800; }
.stat-label { font-size: 11.5px; color: #86a5c8; }

.panel {
  padding: 20px;
  border-radius: 14px;
  background: rgba(10, 21, 40, 0.75);
  border: 1px solid rgba(73, 151, 208, 0.18);
  margin-bottom: 16px;
}
.panel h2 { margin: 0 0 12px; font-size: 14px; color: #cddcee; }
.panel h2.spaced { margin-top: 28px; }
.panel-head { display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 10px; margin-bottom: 10px; }
.panel-head h2 { margin: 0; }
.panel-tools { display: flex; gap: 8px; flex-wrap: wrap; align-items: center; }
.panel-tools input,
.panel-tools select {
  padding: 8px 10px;
  border-radius: 9px;
  border: 1px solid rgba(73, 151, 208, 0.25);
  background: #0a162a;
  color: #eaf1fb;
  font: inherit;
  font-size: 12px;
}
.check { display: flex; gap: 6px; align-items: center; font-size: 12px; color: #86a5c8; }
.hint { margin: 0 0 12px; font-size: 12px; color: #6682a7; }

.bar-row { display: grid; grid-template-columns: 100px 1fr 30px; align-items: center; gap: 10px; margin-bottom: 10px; font-size: 12px; }
.bar-label { color: #9cc6ea; }
.bar-track { height: 8px; border-radius: 999px; background: rgba(73, 151, 208, 0.1); overflow: hidden; }
.bar-fill { height: 100%; background: linear-gradient(90deg, #2f6fb8, #4997d0); border-radius: 999px; }
.bar-count { text-align: right; font-weight: 700; }
.empty { color: #6682a7; font-size: 12px; }

.table-wrap { overflow-x: auto; border-radius: 12px; border: 1px solid rgba(73, 151, 208, 0.15); }
table { width: 100%; border-collapse: collapse; font-size: 12px; }
th {
  text-align: left;
  padding: 10px;
  color: #86a5c8;
  font-weight: 600;
  background: rgba(73, 151, 208, 0.06);
  white-space: nowrap;
}
td { padding: 9px 10px; border-top: 1px solid rgba(73, 151, 208, 0.08); color: #cddcee; vertical-align: top; }
tr.own td { background: rgba(73, 151, 208, 0.07); }
.sub { display: block; font-size: 11px; color: #6682a7; }
.nowrap { white-space: nowrap; }
.center { text-align: center; }
.mono { font-family: ui-monospace, monospace; font-size: 11px; }
.actions { display: flex; gap: 6px; flex-wrap: wrap; }
.own-tag {
  margin-left: 6px;
  padding: 1px 7px;
  border-radius: 999px;
  background: rgba(73, 151, 208, 0.2);
  font-size: 10px;
  color: #9cc6ea;
}

.badge {
  padding: 2px 8px;
  border-radius: 999px;
  font-size: 10.5px;
  font-weight: 700;
  background: rgba(73, 151, 208, 0.12);
  white-space: nowrap;
}
.badge.ok { background: rgba(34, 197, 94, 0.15); color: #86efac; }
.badge.idle { background: rgba(250, 204, 21, 0.12); color: #fde68a; }
.badge.warn { background: rgba(251, 146, 60, 0.15); color: #fdba74; }
.badge.fail { background: rgba(248, 113, 113, 0.15); color: #fca5a5; }

@media (max-width: 720px) {
  .audit-page { padding: 16px; }
}
</style>
