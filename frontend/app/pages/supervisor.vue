<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { apiFetch } from '~/services/api'

definePageMeta({ middleware: 'staff' })

interface Overview {
  totalUsers: number
  activeUsers: number
  usersByRole: { role: string, count: number }[]
  totalAnalyses: number
  analysesLast7Days: number
  analysesByLanguage: { language: string, count: number }[]
  analysesByStatus: { status: string, count: number }[]
  recentAnalyses: { id: string, fileName: string, language: string, status: string, createdAt: string, nickname: string }[]
  recentUsers: { id: string, nickname: string, email: string, roleName: string, createdAt: string }[]
}

const overview = ref<Overview | null>(null)
const isAdmin = ref(false)
const loading = ref(true)
const error = ref('')

const languageLabels: Record<string, string> = {
  ES: 'Español', EN: 'Inglés', RU: 'Ruso', ZH: 'Chino', AR: 'Árabe'
}

const statusLabels: Record<string, string> = {
  PENDING: 'Pendiente', PROCESSING: 'Procesando', COMPLETED: 'Completado', FAILED: 'Falló'
}

async function load() {
  loading.value = true
  error.value = ''

  try {
    overview.value = await apiFetch<Overview>('/Stats/overview')
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'No fue posible cargar las estadísticas.'
  } finally {
    loading.value = false
  }
}

onMounted(() => {
  isAdmin.value = getAuthRole() === 'ADMIN'
  load()
})
</script>

<template>
  <main class="supervisor-page">
    <header class="topbar">
      <div class="brand">
        <span class="brand-mark">L</span>
        <div>
          <h1>Léxica</h1>
          <p>Panel de supervisión</p>
        </div>
      </div>

      <div class="topbar-actions">
        <NuxtLink to="/dashboard" class="link-button">Análisis</NuxtLink>
        <NuxtLink to="/users" class="link-button">Usuarios</NuxtLink>
        <NuxtLink v-if="isAdmin" to="/auditoria" class="link-button">Auditoría</NuxtLink>
        <NuxtLink v-if="isAdmin" to="/solicitudes" class="link-button">Solicitudes</NuxtLink>
        <NuxtLink to="/perfil" class="link-button">Mi perfil</NuxtLink>
      </div>
    </header>

    <div v-if="loading" class="state">Cargando estadísticas...</div>
    <div v-else-if="error" class="state error">{{ error }}</div>

    <template v-else-if="overview">
      <section class="cards">
        <div class="stat-card">
          <span class="stat-value">{{ overview.totalUsers }}</span>
          <span class="stat-label">Usuarios registrados</span>
        </div>
        <div class="stat-card">
          <span class="stat-value">{{ overview.activeUsers }}</span>
          <span class="stat-label">Usuarios activos</span>
        </div>
        <div class="stat-card">
          <span class="stat-value">{{ overview.totalAnalyses }}</span>
          <span class="stat-label">Análisis totales</span>
        </div>
        <div class="stat-card">
          <span class="stat-value">{{ overview.analysesLast7Days }}</span>
          <span class="stat-label">Análisis (últimos 7 días)</span>
        </div>
      </section>

      <section class="grid-2">
        <div class="panel">
          <h2>Usuarios por rol</h2>
          <div v-for="r in overview.usersByRole" :key="r.role" class="bar-row">
            <span class="bar-label">{{ r.role }}</span>
            <div class="bar-track">
              <div
                class="bar-fill"
                :style="{ width: (r.count / Math.max(1, overview.totalUsers) * 100) + '%' }"
              ></div>
            </div>
            <span class="bar-count">{{ r.count }}</span>
          </div>
        </div>

        <div class="panel">
          <h2>Análisis por idioma</h2>
          <div v-for="l in overview.analysesByLanguage" :key="l.language" class="bar-row">
            <span class="bar-label">{{ languageLabels[l.language] ?? l.language }}</span>
            <div class="bar-track">
              <div
                class="bar-fill"
                :style="{ width: (l.count / Math.max(1, overview.totalAnalyses) * 100) + '%' }"
              ></div>
            </div>
            <span class="bar-count">{{ l.count }}</span>
          </div>
          <p v-if="overview.analysesByLanguage.length === 0" class="empty">Todavía no hay análisis.</p>
        </div>
      </section>

      <section class="grid-2">
        <div class="panel">
          <h2>Actividad reciente</h2>
          <ul class="activity-list">
            <li v-for="a in overview.recentAnalyses" :key="a.id">
              <strong>{{ a.nickname }}</strong> analizó
              <em>{{ a.fileName }}</em>
              ({{ languageLabels[a.language] ?? a.language }}) ·
              <span class="tag">{{ statusLabels[a.status] ?? a.status }}</span>
              <span class="when">{{ new Date(a.createdAt).toLocaleString('es-GT') }}</span>
            </li>
          </ul>
          <p v-if="overview.recentAnalyses.length === 0" class="empty">Sin actividad todavía.</p>
        </div>

        <div class="panel">
          <h2>Últimos registros</h2>
          <ul class="activity-list">
            <li v-for="u in overview.recentUsers" :key="u.id">
              <strong>{{ u.nickname }}</strong> ({{ u.roleName }}) · {{ u.email }}
              <span class="when">{{ new Date(u.createdAt).toLocaleDateString('es-GT') }}</span>
            </li>
          </ul>
        </div>
      </section>
    </template>
  </main>
</template>

<style scoped>
.supervisor-page {
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
  margin-bottom: 28px;
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

.state { text-align: center; padding: 40px; color: #86a5c8; }
.state.error { color: #fca5a5; }

.cards {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(140px, 1fr));
  gap: 12px;
  margin-bottom: 20px;
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

.stat-value { font-size: 26px; font-weight: 800; color: #eaf1fb; }
.stat-label { font-size: 11.5px; color: #86a5c8; }

.grid-2 {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
  margin-bottom: 16px;
}

.panel {
  padding: 20px;
  border-radius: 14px;
  background: rgba(10, 21, 40, 0.75);
  border: 1px solid rgba(73, 151, 208, 0.18);
}

.panel h2 { margin: 0 0 14px; font-size: 14px; color: #cddcee; }

.bar-row {
  display: grid;
  grid-template-columns: 90px 1fr 30px;
  align-items: center;
  gap: 10px;
  margin-bottom: 10px;
  font-size: 12px;
}

.bar-label { color: #9cc6ea; }
.bar-track {
  height: 8px;
  border-radius: 999px;
  background: rgba(73, 151, 208, 0.1);
  overflow: hidden;
}
.bar-fill {
  height: 100%;
  background: linear-gradient(90deg, #2f6fb8, #4997d0);
  border-radius: 999px;
}
.bar-count { text-align: right; color: #cddcee; font-weight: 700; }

.activity-list { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 10px; }
.activity-list li {
  font-size: 12.5px;
  color: #cddcee;
  padding-bottom: 10px;
  border-bottom: 1px solid rgba(73, 151, 208, 0.1);
}
.activity-list em { color: #9cc6ea; font-style: normal; }
.activity-list .tag {
  padding: 1px 8px;
  border-radius: 999px;
  background: rgba(73, 151, 208, 0.15);
  font-size: 10.5px;
  font-weight: 700;
}
.activity-list .when { display: block; margin-top: 2px; color: #6682a7; font-size: 11px; }

.empty { color: #6682a7; font-size: 12px; }

@media (max-width: 720px) {
  .grid-2 { grid-template-columns: 1fr; }
}
</style>
