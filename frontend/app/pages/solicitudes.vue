<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { apiFetch } from '~/services/api'

// Revisión de solicitudes de corrección (solo ADMIN; el backend también lo exige).
definePageMeta({ middleware: 'admin' })

type Status = 'PENDING' | 'APPROVED' | 'REJECTED'

interface RequestRow {
  id: string
  status: Status
  reason: string
  createdAt: string
  reviewedAt: string | null
  reviewer: string | null
  requestedEmail: string | null
  requestedBirthDate: string | null
  user: {
    id: string
    nickname: string
    currentEmail: string
    currentBirthDate: string
    isActive: boolean
    deleted: boolean
  }
}

const statusLabels: Record<Status, string> = { PENDING: 'Pendiente', APPROVED: 'Aprobada', REJECTED: 'Rechazada' }

const filter = ref<'' | Status>('PENDING')
const rows = ref<RequestRow[]>([])
const loading = ref(false)
const fb = reactive({ ok: '', err: '' })
const selected = ref<RequestRow | null>(null)
const comment = ref('')
const busy = ref(false)

function formatDate(value: string | null) {
  if (!value) return '—'
  const [y, m, d] = value.slice(0, 10).split('-')
  return `${d}/${m}/${y}`
}

function formatDateTime(value: string | null) {
  return value ? new Date(value).toLocaleString('es-GT', { dateStyle: 'short', timeStyle: 'short' }) : '—'
}

async function load() {
  loading.value = true
  fb.err = ''

  try {
    rows.value = await apiFetch<RequestRow[]>(`/ProfileChangeRequests${filter.value ? `?status=${filter.value}` : ''}`)
    if (selected.value) {
      selected.value = rows.value.find(r => r.id === selected.value?.id) ?? null
    }
  } catch (err) {
    fb.err = err instanceof Error ? err.message : 'No fue posible cargar las solicitudes.'
  } finally {
    loading.value = false
  }
}

function open(row: RequestRow) {
  selected.value = row
  comment.value = ''
  fb.ok = ''
  fb.err = ''
}

async function review(action: 'approve' | 'reject') {
  if (!selected.value) return

  const text = action === 'approve'
    ? `¿Aprobar y aplicar los cambios a ${selected.value.user.nickname}?`
    : `¿Rechazar la solicitud de ${selected.value.user.nickname}?`

  if (!confirm(text)) return

  busy.value = true
  fb.ok = ''
  fb.err = ''

  try {
    const result = await apiFetch<{ message: string }>(`/ProfileChangeRequests/${selected.value.id}/${action}`, {
      method: 'POST',
      body: JSON.stringify({ comment: comment.value.trim() || null }),
    })
    fb.ok = result.message
    await load()
  } catch (err) {
    fb.err = err instanceof Error ? err.message : 'No fue posible revisar la solicitud.'
  } finally {
    busy.value = false
  }
}

onMounted(load)
</script>

<template>
  <main class="requests-page">
    <header class="topbar">
      <div class="brand">
        <span class="brand-mark">L</span>
        <div>
          <h1>Léxica</h1>
          <p>Solicitudes de corrección</p>
        </div>
      </div>
      <div class="topbar-actions">
        <NuxtLink to="/dashboard" class="link-button">Análisis</NuxtLink>
        <NuxtLink to="/users" class="link-button">Usuarios</NuxtLink>
        <NuxtLink to="/auditoria" class="link-button">Auditoría</NuxtLink>
        <NuxtLink to="/perfil" class="link-button">Mi perfil</NuxtLink>
      </div>
    </header>

    <div v-if="fb.ok" class="banner ok">{{ fb.ok }}</div>
    <div v-if="fb.err" class="banner error">{{ fb.err }}</div>

    <div class="layout">
      <section class="panel">
        <div class="panel-head">
          <h2>Solicitudes</h2>
          <select v-model="filter" @change="load">
            <option value="PENDING">Pendientes</option>
            <option value="APPROVED">Aprobadas</option>
            <option value="REJECTED">Rechazadas</option>
            <option value="">Todas</option>
          </select>
        </div>

        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Usuario</th>
                <th>Solicita</th>
                <th>Estado</th>
                <th>Fecha</th>
              </tr>
            </thead>
            <tbody>
              <tr v-if="rows.length === 0">
                <td colspan="4" class="state">{{ loading ? 'Cargando...' : 'No hay solicitudes.' }}</td>
              </tr>
              <tr v-for="r in rows" :key="r.id" :class="{ active: selected?.id === r.id }" @click="open(r)">
                <td><strong>{{ r.user.nickname }}</strong><span class="sub">{{ r.user.currentEmail }}</span></td>
                <td>
                  <span v-if="r.requestedEmail" class="sub-line">Correo</span>
                  <span v-if="r.requestedBirthDate" class="sub-line">Fecha de nacimiento</span>
                </td>
                <td><span class="badge" :class="r.status.toLowerCase()">{{ statusLabels[r.status] }}</span></td>
                <td class="nowrap">{{ formatDateTime(r.createdAt) }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>

      <section class="panel detail">
        <template v-if="selected">
          <h2>Detalle</h2>
          <p class="who">
            <strong>{{ selected.user.nickname }}</strong>
            <span v-if="selected.user.deleted" class="badge rejected">Cuenta eliminada</span>
            <span v-else-if="!selected.user.isActive" class="badge pending">Cuenta desactivada</span>
          </p>

          <table class="diff">
            <thead><tr><th /><th v-if="selected.status === 'PENDING'">Actual</th><th>Solicitado</th></tr></thead>
            <tbody>
              <tr v-if="selected.requestedEmail">
                <th>Correo</th><td v-if="selected.status === 'PENDING'">{{ selected.user.currentEmail }}</td><td class="new">{{ selected.requestedEmail }}</td>
              </tr>
              <tr v-if="selected.requestedBirthDate">
                <th>Fecha de nacimiento</th><td v-if="selected.status === 'PENDING'">{{ formatDate(selected.user.currentBirthDate) }}</td><td class="new">{{ formatDate(selected.requestedBirthDate) }}</td>
              </tr>
            </tbody>
          </table>

          <h3>Motivo del usuario</h3>
          <p class="reason">{{ selected.reason }}</p>

          <p class="meta">Enviada: {{ formatDateTime(selected.createdAt) }}</p>
          <p v-if="selected.status !== 'PENDING'" class="meta">
            {{ statusLabels[selected.status] }} por {{ selected.reviewer ?? '—' }} el {{ formatDateTime(selected.reviewedAt) }}
          </p>

          <template v-if="selected.status === 'PENDING'">
            <label class="comment">
              Comentario (opcional, queda en la bitácora)
              <textarea v-model="comment" rows="2" maxlength="500" />
            </label>
            <div class="actions">
              <button type="button" class="approve" :disabled="busy" @click="review('approve')">Aprobar y aplicar</button>
              <button type="button" class="reject" :disabled="busy" @click="review('reject')">Rechazar</button>
            </div>
          </template>
        </template>
        <p v-else class="state">Selecciona una solicitud para ver el detalle.</p>
      </section>
    </div>
  </main>
</template>

<style scoped>
.requests-page { min-height: 100vh; background:
    linear-gradient(180deg, rgba(6, 12, 28, 0.55) 0%, rgba(6, 12, 28, 0.78) 100%),
    url('/images/apartado2.png') center / cover no-repeat fixed,
    #07101f; color: #eaf1fb; font-family: 'Poppins', 'Segoe UI', system-ui, sans-serif; padding: 24px; }
.topbar { display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 14px; margin-bottom: 20px; }
.brand { display: flex; align-items: center; gap: 12px; }
.brand-mark {
  width: 38px; height: 38px; display: grid; place-items: center; border-radius: 11px;
  background: linear-gradient(135deg, rgba(73, 151, 208, 0.3), rgba(73, 151, 208, 0.05));
  border: 1px solid rgba(73, 151, 208, 0.35); color: #9cc6ea; font-weight: 800;
}
.brand h1 { margin: 0; font-size: 17px; }
.brand p { margin: 1px 0 0; font-size: 10px; color: #6682a7; }
.topbar-actions { display: flex; gap: 10px; flex-wrap: wrap; }
.link-button { padding: 9px 14px; border-radius: 9px; border: 1px solid rgba(73, 151, 208, 0.25); background: rgba(73, 151, 208, 0.06); color: #a9c5e3; text-decoration: none; font-size: 13px; font-weight: 600; }

.banner { padding: 10px 14px; border-radius: 10px; font-size: 12.5px; margin-bottom: 14px; }
.banner.ok { background: rgba(34, 197, 94, 0.12); color: #86efac; border: 1px solid rgba(34, 197, 94, 0.25); }
.banner.error { background: rgba(127, 29, 29, 0.15); color: #fca5a5; border: 1px solid rgba(248, 113, 113, 0.3); }

.layout { display: grid; grid-template-columns: minmax(0, 1.4fr) minmax(280px, 1fr); gap: 16px; align-items: start; }
.panel { padding: 20px; border-radius: 14px; background: rgba(10, 21, 40, 0.75); border: 1px solid rgba(73, 151, 208, 0.18); }
.panel-head { display: flex; justify-content: space-between; align-items: center; gap: 10px; margin-bottom: 12px; }
h2 { margin: 0; font-size: 14px; color: #cddcee; }
h3 { margin: 16px 0 6px; font-size: 12.5px; color: #86a5c8; }
select, textarea { padding: 8px 10px; border-radius: 9px; border: 1px solid rgba(73, 151, 208, 0.25); background: #0a162a; color: #eaf1fb; font: inherit; font-size: 12px; }

.table-wrap { overflow-x: auto; border-radius: 12px; border: 1px solid rgba(73, 151, 208, 0.15); }
table { width: 100%; border-collapse: collapse; font-size: 12px; }
th { text-align: left; padding: 10px; color: #86a5c8; font-weight: 600; background: rgba(73, 151, 208, 0.06); white-space: nowrap; }
td { padding: 9px 10px; border-top: 1px solid rgba(73, 151, 208, 0.08); color: #cddcee; vertical-align: top; }
tbody tr { cursor: pointer; }
tbody tr:hover td, tbody tr.active td { background: rgba(73, 151, 208, 0.08); }
.sub { display: block; font-size: 11px; color: #6682a7; }
.sub-line { display: block; }
.nowrap { white-space: nowrap; }
.state { text-align: center; padding: 24px; color: #86a5c8; }

.badge { padding: 2px 8px; border-radius: 999px; font-size: 10.5px; font-weight: 700; white-space: nowrap; }
.badge.pending { background: rgba(250, 204, 21, 0.12); color: #fde68a; }
.badge.approved { background: rgba(34, 197, 94, 0.15); color: #86efac; }
.badge.rejected { background: rgba(248, 113, 113, 0.15); color: #fca5a5; }

.detail h2 { margin-bottom: 10px; }
.who { display: flex; gap: 8px; align-items: center; margin: 0 0 12px; }
.diff { border: 1px solid rgba(73, 151, 208, 0.15); border-radius: 10px; overflow: hidden; }
.diff tbody tr { cursor: default; }
.diff tbody tr:hover td { background: none; }
.diff .new { color: #86efac; font-weight: 600; }
.reason { margin: 0; padding: 10px 12px; border-radius: 10px; background: rgba(5, 11, 22, 0.6); font-size: 12.5px; white-space: pre-wrap; }
.meta { margin: 8px 0 0; font-size: 11.5px; color: #6682a7; }
.comment { display: flex; flex-direction: column; gap: 6px; margin-top: 14px; font-size: 11.5px; color: #86a5c8; }
.actions { display: flex; gap: 8px; margin-top: 12px; }
button { border: 0; border-radius: 9px; padding: 9px 13px; cursor: pointer; font: inherit; font-size: 12.5px; font-weight: 600; }
button:disabled { opacity: 0.45; cursor: not-allowed; }
.approve { background: rgba(34, 197, 94, 0.2); color: #86efac; }
.reject { background: rgba(248, 113, 113, 0.15); color: #fca5a5; }

@media (max-width: 900px) { .layout { grid-template-columns: 1fr; } .requests-page { padding: 16px; } }
</style>
