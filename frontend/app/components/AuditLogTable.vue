<script setup lang="ts">
import { ref, reactive, watch, onMounted } from 'vue'
import { apiFetch } from '~/services/api'

// Tabla de bitácora reutilizable: cada pestaña de /auditoria la usa con una
// categoría fija (o sin categoría para la bitácora completa).
const props = defineProps<{
  category?: string
  userId?: string | null
  userLabel?: string | null
  showCategoryFilter?: boolean
  /** 'true' / 'false' para arrancar filtrando por resultado. */
  initialSuccess?: string
  pageSize?: number
}>()

const emit = defineEmits<{ (e: 'clear-user'): void }>()

interface ActionInfo { action: string, category: string, label: string }
interface PersonRef { id: string, nickname: string, email: string }
interface LogItem {
  id: number
  createdAt: string
  action: string
  label: string
  category: string
  success: boolean | null
  details: Record<string, unknown> | null
  ip: string | null
  userAgent: string | null
  actor: PersonRef | null
  target: PersonRef | null
}

const catalog = ref<ActionInfo[]>([])
const items = ref<LogItem[]>([])
const total = ref(0)
const loading = ref(false)
const error = ref('')
const expanded = ref<number | null>(null)

const filters = reactive({
  category: '',
  action: '',
  success: props.initialSuccess ?? '',
  from: '',
  to: '',
  search: '',
  page: 1,
  pageSize: props.pageSize ?? 25
})

const categoryLabels: Record<string, string> = {
  SESIONES: 'Sesiones',
  USUARIOS: 'Usuarios',
  PERFIL: 'Perfil',
  SEGURIDAD: 'Seguridad',
  ANALISIS: 'Análisis',
  SISTEMA: 'Sistema'
}

const fieldLabels: Record<string, string> = {
  email: 'Correo',
  phone: 'Teléfono',
  birthDate: 'Fecha de nacimiento',
  nickname: 'Nickname',
  notificationMethod: 'Notificaciones',
  method: 'Método',
  reason: 'Motivo',
  similarity: 'Similitud facial',
  sessionsClosed: 'Sesiones cerradas',
  otherSessionsClosed: 'Otras sesiones cerradas',
  from: 'De',
  to: 'A',
  role: 'Rol',
  channel: 'Canal',
  credentialId: 'Credencial',
  previousDeactivated: 'Credenciales anteriores desactivadas',
  detectionConfidence: 'Confianza de detección',
  analysisId: 'Análisis',
  fileName: 'Archivo',
  language: 'Idioma',
  sessionId: 'Sesión',
  scope: 'Alcance',
  contentType: 'Tipo de archivo'
}

const actionsForFilter = () =>
  catalog.value.filter(a => !props.category || a.category === props.category)

async function loadCatalog() {
  try {
    const data = await apiFetch<{ actions: ActionInfo[] }>('/Audit/catalog')
    catalog.value = data.actions
  } catch {
    catalog.value = []
  }
}

async function load() {
  loading.value = true
  error.value = ''

  const params = new URLSearchParams()
  const category = props.category || filters.category
  if (category) params.set('category', category)
  if (filters.action) params.set('action', filters.action)
  if (filters.success) params.set('success', filters.success)
  if (filters.from) params.set('from', new Date(filters.from).toISOString())
  if (filters.to) params.set('to', new Date(filters.to).toISOString())
  if (filters.search.trim()) params.set('search', filters.search.trim())
  if (props.userId) params.set('userId', props.userId)
  params.set('page', String(filters.page))
  params.set('pageSize', String(filters.pageSize))

  try {
    const data = await apiFetch<{ items: LogItem[], total: number }>(`/Audit/logs?${params}`)
    items.value = data.items
    total.value = data.total
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'No fue posible cargar la bitácora.'
  } finally {
    loading.value = false
  }
}

function applyFilters() {
  filters.page = 1
  load()
}

function resetFilters() {
  Object.assign(filters, { category: '', action: '', success: props.initialSuccess ?? '', from: '', to: '', search: '', page: 1 })
  load()
}

function goTo(page: number) {
  filters.page = page
  load()
}

const totalPages = () => Math.max(1, Math.ceil(total.value / filters.pageSize))

function formatDate(value: string) {
  return new Date(value).toLocaleString('es-GT', { dateStyle: 'short', timeStyle: 'medium' })
}

function shortAgent(ua: string | null) {
  if (!ua) return '—'
  const browser = /Edg\//.test(ua) ? 'Edge'
    : /Firefox\//.test(ua) ? 'Firefox'
    : /Chrome\//.test(ua) ? 'Chrome'
    : /Safari\//.test(ua) ? 'Safari'
    : /PostmanRuntime/.test(ua) ? 'Postman'
    : /curl\//.test(ua) ? 'curl'
    : 'Otro'
  const os = /Windows/.test(ua) ? 'Windows'
    : /Android/.test(ua) ? 'Android'
    : /iPhone|iPad/.test(ua) ? 'iOS'
    : /Mac OS/.test(ua) ? 'macOS'
    : /Linux/.test(ua) ? 'Linux'
    : ''
  return os ? `${browser} · ${os}` : browser
}

function formatValue(value: unknown): string {
  if (value === null || value === undefined || value === '') return '—'
  if (typeof value === 'boolean') return value ? 'Sí' : 'No'
  if (typeof value === 'object') return JSON.stringify(value)
  return String(value)
}

// Convierte "details" en líneas legibles. Los cambios de campos se muestran
// como "Campo: antes → después".
function detailLines(details: Record<string, unknown> | null): string[] {
  if (!details) return []

  const lines: string[] = []

  for (const [key, value] of Object.entries(details)) {
    if (value === null || value === undefined) continue

    if (key === 'changes' && Array.isArray(value)) {
      if (value.length === 0) {
        lines.push('Sin cambios en los datos')
      }
      for (const change of value as { field: string, from: unknown, to: unknown }[]) {
        lines.push(`${fieldLabels[change.field] ?? change.field}: ${formatValue(change.from)} → ${formatValue(change.to)}`)
      }
      continue
    }

    lines.push(`${fieldLabels[key] ?? key}: ${formatValue(value)}`)
  }

  return lines
}

function personLabel(person: PersonRef | null) {
  return person ? person.nickname : '—'
}

watch(() => [props.userId, props.category], () => {
  filters.page = 1
  load()
})

onMounted(() => {
  loadCatalog()
  load()
})

defineExpose({ reload: load })
</script>

<template>
  <div class="log-table">
    <div class="filters">
      <select v-if="showCategoryFilter && !category" v-model="filters.category" @change="filters.action = ''">
        <option value="">Todas las categorías</option>
        <option v-for="(label, key) in categoryLabels" :key="key" :value="key">{{ label }}</option>
      </select>

      <select v-model="filters.action">
        <option value="">Todas las acciones</option>
        <option
          v-for="a in actionsForFilter().filter(a => !filters.category || a.category === filters.category)"
          :key="a.action"
          :value="a.action"
        >
          {{ a.label }}
        </option>
      </select>

      <select v-model="filters.success">
        <option value="">Éxito y fallo</option>
        <option value="true">Solo exitosos</option>
        <option value="false">Solo fallidos</option>
      </select>

      <label class="date">Desde <input v-model="filters.from" type="datetime-local"></label>
      <label class="date">Hasta <input v-model="filters.to" type="datetime-local"></label>

      <input
        v-model="filters.search"
        type="search"
        maxlength="100"
        placeholder="Buscar por correo o nickname"
        @keyup.enter="applyFilters"
      >

      <button type="button" class="primary" @click="applyFilters">Filtrar</button>
      <button type="button" class="ghost" @click="resetFilters">Limpiar</button>
    </div>

    <div v-if="userId" class="user-chip">
      Mostrando solo eventos de <strong>{{ userLabel }}</strong>
      <button type="button" @click="emit('clear-user')">✕ Quitar</button>
    </div>

    <div v-if="error" class="state error">{{ error }}</div>

    <div class="table-wrap">
      <table>
        <thead>
          <tr>
            <th>Fecha</th>
            <th>Acción</th>
            <th>Resultado</th>
            <th>Quién</th>
            <th>Usuario afectado</th>
            <th>IP</th>
            <th>Dispositivo</th>
            <th />
          </tr>
        </thead>
        <tbody>
          <tr v-if="loading">
            <td colspan="8" class="state">Cargando...</td>
          </tr>
          <tr v-else-if="items.length === 0">
            <td colspan="8" class="state">No hay eventos con estos filtros.</td>
          </tr>
          <template v-for="item in items" v-else :key="item.id">
            <tr :class="{ failed: item.success === false }">
              <td class="nowrap">{{ formatDate(item.createdAt) }}</td>
              <td>
                <span class="action">{{ item.label }}</span>
                <span class="category">{{ categoryLabels[item.category] ?? item.category }}</span>
              </td>
              <td>
                <span v-if="item.success === true" class="badge ok">Éxito</span>
                <span v-else-if="item.success === false" class="badge fail">Fallo</span>
                <span v-else class="badge">—</span>
              </td>
              <td :title="item.actor?.email">{{ item.actor ? personLabel(item.actor) : 'Anónimo' }}</td>
              <td :title="item.target?.email">{{ personLabel(item.target) }}</td>
              <td class="mono">{{ item.ip ?? '—' }}</td>
              <td :title="item.userAgent ?? ''">{{ shortAgent(item.userAgent) }}</td>
              <td>
                <button
                  v-if="item.details"
                  type="button"
                  class="link"
                  @click="expanded = expanded === item.id ? null : item.id"
                >
                  {{ expanded === item.id ? 'Ocultar' : 'Detalle' }}
                </button>
              </td>
            </tr>
            <tr v-if="expanded === item.id" class="detail-row">
              <td colspan="8">
                <ul>
                  <li v-for="(line, i) in detailLines(item.details)" :key="i">{{ line }}</li>
                </ul>
                <p v-if="item.actor" class="muted">Quién: {{ item.actor.nickname }} ({{ item.actor.email }})</p>
                <p v-if="item.target" class="muted">Afectado: {{ item.target.nickname }} ({{ item.target.email }})</p>
                <p v-if="item.userAgent" class="muted">Navegador: {{ item.userAgent }}</p>
              </td>
            </tr>
          </template>
        </tbody>
      </table>
    </div>

    <div class="pager">
      <span>{{ total }} evento(s) · página {{ filters.page }} de {{ totalPages() }}</span>
      <div>
        <button type="button" class="ghost" :disabled="filters.page <= 1 || loading" @click="goTo(filters.page - 1)">← Anterior</button>
        <button type="button" class="ghost" :disabled="filters.page >= totalPages() || loading" @click="goTo(filters.page + 1)">Siguiente →</button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.filters {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-bottom: 12px;
  align-items: center;
}

.filters select,
.filters input {
  padding: 8px 10px;
  border-radius: 9px;
  border: 1px solid rgba(73, 151, 208, 0.25);
  background: #0a162a;
  color: #eaf1fb;
  font: inherit;
  font-size: 12px;
}

.filters input[type="search"] { min-width: 200px; flex: 1; }

.date {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 11px;
  color: #86a5c8;
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
.primary { background: linear-gradient(135deg, #2f6fb8, #4997d0); color: white; }
.ghost { background: rgba(73, 151, 208, 0.1); color: #cddcee; }
.link { background: none; color: #6faad8; padding: 4px 6px; }

.user-chip {
  display: inline-flex;
  align-items: center;
  gap: 10px;
  padding: 6px 10px;
  border-radius: 999px;
  background: rgba(73, 151, 208, 0.12);
  font-size: 12px;
  margin-bottom: 10px;
}
.user-chip button { padding: 2px 8px; background: rgba(0, 0, 0, 0.25); color: #cddcee; }

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
tr.failed td { background: rgba(127, 29, 29, 0.12); }

.nowrap { white-space: nowrap; }
.mono { font-family: ui-monospace, monospace; font-size: 11px; }
.action { display: block; font-weight: 600; color: #eaf1fb; }
.category { font-size: 10px; color: #6682a7; text-transform: uppercase; letter-spacing: 0.04em; }

.badge {
  padding: 2px 8px;
  border-radius: 999px;
  font-size: 10.5px;
  font-weight: 700;
  background: rgba(73, 151, 208, 0.12);
}
.badge.ok { background: rgba(34, 197, 94, 0.15); color: #86efac; }
.badge.fail { background: rgba(248, 113, 113, 0.15); color: #fca5a5; }

.detail-row td { background: rgba(5, 11, 22, 0.6); }
.detail-row ul { margin: 0 0 6px; padding-left: 18px; }
.detail-row li { margin-bottom: 3px; }
.muted { margin: 2px 0; color: #6682a7; font-size: 11px; word-break: break-all; }

.state { text-align: center; padding: 24px; color: #86a5c8; }
.state.error { color: #fca5a5; }

.pager {
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 10px;
  margin-top: 12px;
  font-size: 12px;
  color: #86a5c8;
}
.pager div { display: flex; gap: 8px; }
</style>
