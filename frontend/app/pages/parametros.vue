<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { apiFetch } from '~/services/api'

// Parametrización del sistema (solo ADMIN): diccionario de entidades que usa
// el analizador léxico para reconocer nombres de personas, países, etc.
definePageMeta({ middleware: 'admin' })

interface Entry {
  id: string
  value: string
  canonicalValue: string | null
  entityType: string
  language: string | null
  source: string | null
  createdAt: string
}

const typeLabels: Record<string, string> = {
  PERSON: 'Persona', COUNTRY: 'País', CITY: 'Ciudad', ORGANIZATION: 'Organización',
  BRAND: 'Marca', PRODUCT: 'Producto', LOCATION: 'Lugar',
}
const languageLabels: Record<string, string> = { ES: 'Español', EN: 'Inglés', RU: 'Ruso', ZH: 'Chino', AR: 'Árabe' }

const options = reactive({ entityTypes: [] as string[], languages: [] as string[] })
const filters = reactive({ entityType: '', language: '', search: '', page: 1, pageSize: 50 })
const items = ref<Entry[]>([])
const total = ref(0)
const summary = ref<{ entityType: string, count: number }[]>([])
const loading = ref(false)
const fb = reactive({ ok: '', err: '' })

const form = reactive({ value: '', canonicalValue: '', entityType: 'PERSON', language: 'ES' })
const saving = ref(false)

const totalPages = () => Math.max(1, Math.ceil(total.value / filters.pageSize))

async function load() {
  loading.value = true
  fb.err = ''

  const params = new URLSearchParams({ page: String(filters.page), pageSize: String(filters.pageSize) })
  if (filters.entityType) params.set('entityType', filters.entityType)
  if (filters.language) params.set('language', filters.language)
  if (filters.search.trim()) params.set('search', filters.search.trim())

  try {
    const data = await apiFetch<{ items: Entry[], total: number, summary: { entityType: string, count: number }[] }>(`/EntityDictionary?${params}`)
    items.value = data.items
    total.value = data.total
    summary.value = data.summary
  } catch (err) {
    fb.err = err instanceof Error ? err.message : 'No fue posible cargar el diccionario.'
  } finally {
    loading.value = false
  }
}

function applyFilters() {
  filters.page = 1
  load()
}

async function add() {
  fb.ok = ''
  fb.err = ''
  saving.value = true

  try {
    const result = await apiFetch<{ message: string }>('/EntityDictionary', {
      method: 'POST',
      body: JSON.stringify({
        value: form.value,
        canonicalValue: form.canonicalValue || null,
        entityType: form.entityType,
        language: form.language || null,
      }),
    })
    fb.ok = result.message
    form.value = ''
    form.canonicalValue = ''
    await load()
  } catch (err) {
    fb.err = err instanceof Error ? err.message : 'No fue posible agregar la entrada.'
  } finally {
    saving.value = false
  }
}

async function remove(entry: Entry) {
  if (!confirm(`¿Eliminar "${entry.value}" (${typeLabels[entry.entityType] ?? entry.entityType}) del diccionario?`)) return

  fb.ok = ''
  fb.err = ''

  try {
    const result = await apiFetch<{ message: string }>(`/EntityDictionary/${entry.id}`, { method: 'DELETE' })
    fb.ok = result.message
    await load()
  } catch (err) {
    fb.err = err instanceof Error ? err.message : 'No fue posible eliminar la entrada.'
  }
}

onMounted(async () => {
  try {
    Object.assign(options, await apiFetch<typeof options>('/EntityDictionary/options'))
  } catch {
    // Si falla, la lista igual se muestra.
  }
  load()
})
</script>

<template>
  <main class="params-page">
    <header class="topbar">
      <div class="brand">
        <span class="brand-mark">L</span>
        <div>
          <h1>Léxica</h1>
          <p>Parametrización del sistema</p>
        </div>
      </div>
      <nav class="topbar-actions">
        <NuxtLink to="/dashboard" class="link-button">← Análisis</NuxtLink>
        <NuxtLink to="/users" class="link-button">Usuarios</NuxtLink>
        <NuxtLink to="/auditoria" class="link-button">Auditoría</NuxtLink>
        <NuxtLink to="/perfil" class="link-button">Mi perfil</NuxtLink>
      </nav>
    </header>

    <section class="intro">
      <h2>Diccionario de entidades</h2>
      <p>
        Palabras que el analizador léxico reconoce como personas, países, ciudades, organizaciones, marcas,
        productos o lugares. Los cambios aplican desde el siguiente análisis. El analizador compara
        palabra por palabra, así que cada entrada es una sola palabra.
      </p>
      <div class="summary">
        <span v-for="s in summary" :key="s.entityType" class="pill">
          {{ typeLabels[s.entityType] ?? s.entityType }}: <strong>{{ s.count }}</strong>
        </span>
      </div>
    </section>

    <div v-if="fb.ok" class="banner ok">{{ fb.ok }}</div>
    <div v-if="fb.err" class="banner error">{{ fb.err }}</div>

    <div class="layout">
      <section class="panel">
        <div class="filters">
          <select v-model="filters.entityType" @change="applyFilters">
            <option value="">Todos los tipos</option>
            <option v-for="t in options.entityTypes" :key="t" :value="t">{{ typeLabels[t] ?? t }}</option>
          </select>
          <select v-model="filters.language" @change="applyFilters">
            <option value="">Todos los idiomas</option>
            <option value="ALL">Sin idioma (aplica a todos)</option>
            <option v-for="l in options.languages" :key="l" :value="l">{{ languageLabels[l] ?? l }}</option>
          </select>
          <input v-model="filters.search" type="search" maxlength="80" placeholder="Buscar palabra" @keyup.enter="applyFilters">
          <button type="button" class="ghost" @click="applyFilters">Buscar</button>
        </div>

        <div class="table-wrap">
          <table>
            <thead>
              <tr><th>Palabra</th><th>Forma canónica</th><th>Tipo</th><th>Idioma</th><th>Origen</th><th /></tr>
            </thead>
            <tbody>
              <tr v-if="items.length === 0">
                <td colspan="6" class="state">{{ loading ? 'Cargando...' : 'No hay entradas con este filtro.' }}</td>
              </tr>
              <tr v-for="e in items" :key="e.id">
                <td><strong>{{ e.value }}</strong></td>
                <td>{{ e.canonicalValue ?? '—' }}</td>
                <td><span class="pill">{{ typeLabels[e.entityType] ?? e.entityType }}</span></td>
                <td>{{ e.language ? (languageLabels[e.language] ?? e.language) : 'Todos' }}</td>
                <td class="muted">{{ e.source ?? '—' }}</td>
                <td><button type="button" class="danger" @click="remove(e)">Eliminar</button></td>
              </tr>
            </tbody>
          </table>
        </div>

        <div class="pager">
          <span>{{ total }} entrada(s) · página {{ filters.page }} de {{ totalPages() }}</span>
          <div>
            <button type="button" class="ghost" :disabled="filters.page <= 1" @click="filters.page--; load()">← Anterior</button>
            <button type="button" class="ghost" :disabled="filters.page >= totalPages()" @click="filters.page++; load()">Siguiente →</button>
          </div>
        </div>
      </section>

      <form class="panel" @submit.prevent="add">
        <h3>Agregar entrada</h3>
        <label>Palabra
          <input v-model="form.value" type="text" maxlength="80" required placeholder="Ej. Antigua">
        </label>
        <label>Forma canónica (opcional)
          <input v-model="form.canonicalValue" type="text" maxlength="120" placeholder="Ej. Antigua Guatemala">
        </label>
        <label>Tipo
          <select v-model="form.entityType">
            <option v-for="t in options.entityTypes" :key="t" :value="t">{{ typeLabels[t] ?? t }}</option>
          </select>
        </label>
        <label>Idioma
          <select v-model="form.language">
            <option value="">Todos los idiomas</option>
            <option v-for="l in options.languages" :key="l" :value="l">{{ languageLabels[l] ?? l }}</option>
          </select>
        </label>
        <button type="submit" class="primary" :disabled="saving">{{ saving ? 'Guardando...' : 'Agregar' }}</button>
      </form>
    </div>
  </main>
</template>

<style scoped>
.params-page { min-height: 100vh; background:
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

.intro h2 { margin: 0 0 6px; font-size: 20px; }
.intro p { margin: 0 0 12px; max-width: 760px; color: #819ab9; font-size: 13px; line-height: 1.5; }
.summary { display: flex; flex-wrap: wrap; gap: 8px; margin-bottom: 16px; }
.pill { padding: 3px 10px; border-radius: 999px; background: rgba(73, 151, 208, 0.12); color: #9cc6ea; font-size: 11.5px; white-space: nowrap; }

.banner { padding: 10px 14px; border-radius: 10px; font-size: 12.5px; margin-bottom: 14px; }
.banner.ok { background: rgba(34, 197, 94, 0.12); color: #86efac; border: 1px solid rgba(34, 197, 94, 0.25); }
.banner.error { background: rgba(127, 29, 29, 0.15); color: #fca5a5; border: 1px solid rgba(248, 113, 113, 0.3); }

.layout { display: grid; grid-template-columns: minmax(0, 1fr) 300px; gap: 16px; align-items: start; }
.panel { padding: 20px; border-radius: 14px; background: rgba(10, 21, 40, 0.75); border: 1px solid rgba(73, 151, 208, 0.18); }
.panel h3 { margin: 0 0 12px; font-size: 14px; }
.filters { display: flex; flex-wrap: wrap; gap: 8px; margin-bottom: 12px; }
select, input { padding: 9px 11px; border-radius: 9px; border: 1px solid rgba(73, 151, 208, 0.25); background: #0a162a; color: #eaf1fb; font: inherit; font-size: 12.5px; }
.filters input { flex: 1; min-width: 160px; }
form label { display: flex; flex-direction: column; gap: 6px; margin-bottom: 12px; font-size: 12px; font-weight: 600; color: #86a5c8; }

button { border: 0; border-radius: 9px; padding: 9px 13px; cursor: pointer; font: inherit; font-size: 12.5px; font-weight: 600; }
button:disabled { opacity: 0.45; cursor: not-allowed; }
.primary { width: 100%; background: linear-gradient(135deg, #2f6fb8, #4997d0); color: #fff; }
.ghost { background: rgba(73, 151, 208, 0.1); color: #cddcee; }
.danger { padding: 6px 10px; background: rgba(248, 113, 113, 0.15); color: #fca5a5; }

.table-wrap { overflow-x: auto; border-radius: 12px; border: 1px solid rgba(73, 151, 208, 0.15); }
table { width: 100%; border-collapse: collapse; font-size: 12.5px; }
th { text-align: left; padding: 10px; color: #86a5c8; font-weight: 600; background: rgba(73, 151, 208, 0.06); white-space: nowrap; }
td { padding: 9px 10px; border-top: 1px solid rgba(73, 151, 208, 0.08); color: #cddcee; }
.muted { color: #6682a7; }
.state { text-align: center; padding: 24px; color: #86a5c8; }
.pager { display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 10px; margin-top: 12px; font-size: 12px; color: #86a5c8; }
.pager div { display: flex; gap: 8px; }

@media (max-width: 900px) { .layout { grid-template-columns: 1fr; } .params-page { padding: 16px; } }
</style>
