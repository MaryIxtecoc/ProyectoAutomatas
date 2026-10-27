<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { apiFetch, logoutSession } from '~/services/api'

definePageMeta({ middleware: 'auth' })

interface Entity {
  text?: string
  value?: string
  canonical?: string
  canonicalValue?: string
  entityType?: string
  language?: string
}

interface FrequencyWord {
  Word: string
  Count: number
}

interface AnalysisMetrics {
  nounCount?: number
  verbCount?: number
  pronounCount?: number
  stopWordCount?: number
  personNameCount?: number
  uniqueWordCount?: number
  contentWordCount?: number
  lexicalDiversity?: number
  averageWordLength?: number
}

interface AnalysisResult {
  totalWordCount: number
  mostFrequentWords: string | FrequencyWord[]
  leastFrequentWords: string | FrequencyWord[]
  personalPronouns: string | string[]
  personNames: string | string[]
  rootNouns: string | string[]
  rootVerbs: string | string[]
  otherClassifications: string | Record<string, unknown>
  fullResult?: string | Record<string, unknown>

  uniqueWordCount?: number
  metrics?: string | Record<string, unknown>
}

interface AnalysisJob {
  id: string
  fileName: string
  language: string
  status: string
  reportRequested: boolean
  reportPdfPath?: string | null
  createdAt: string
  uploadedAt: string
  processedAt?: string | null
  errorMessage?: string | null
  results?: AnalysisResult[]
}

const route = useRoute()

const loading = ref(true)
const error = ref('')
const job = ref<AnalysisJob | null>(null)
const result = ref<AnalysisResult | null>(null)

const languageNames: Record<string, string> = {
  ES: 'Español',
  EN: 'English',
  RU: 'Русский',
  ZH: '中文',
  AR: 'العربية',
}

function parseArray(value: string | string[] | undefined): string[] {
  if (!value) {
    return []
  }

  if (Array.isArray(value)) {
    return value
  }

  try {
    const parsed = JSON.parse(value)

    return Array.isArray(parsed)
      ? parsed
      : []
  } catch {
    return []
  }
}

function parseObject(
  value: string | Record<string, unknown> | undefined,
): Record<string, unknown> {
  if (!value) {
    return {}
  }

  if (typeof value === 'object') {
    return value
  }

  try {
    const parsed = JSON.parse(value)

    return parsed && typeof parsed === 'object'
      ? parsed
      : {}
  } catch {
    return {}
  }
}

// Busca una llave ignorando mayúsculas/minúsculas
function getCI(obj: Record<string, unknown>, key: string): unknown {
  const found = Object.keys(obj).find(
    (k) => k.toLowerCase() === key.toLowerCase(),
  )

  return found ? obj[found] : undefined
}

function toNumber(value: unknown): number | undefined {
  return typeof value === 'number' ? value : undefined
}

const metrics = computed<AnalysisMetrics>(() => {
  if (!result.value) {
    return {}
  }

  const full = parseObject(result.value.fullResult)

  // El backend guarda las métricas dentro de:
  // FullResult.OtherClassifications
  const otherClassifications = parseObject(
    getCI(full, 'OtherClassifications') as
      | string
      | Record<string, unknown>
      | undefined,
  )

  return {
    nounCount: toNumber(getCI(otherClassifications, 'nounCount')),
    verbCount: toNumber(getCI(otherClassifications, 'verbCount')),
    pronounCount: toNumber(getCI(otherClassifications, 'pronounCount')),
    stopWordCount: toNumber(getCI(otherClassifications, 'stopWordCount')),
    personNameCount: toNumber(
      getCI(otherClassifications, 'personNameCount'),
    ),
    contentWordCount: toNumber(
      getCI(otherClassifications, 'contentWordCount'),
    ),
    lexicalDiversity: toNumber(
      getCI(otherClassifications, 'lexicalDiversity'),
    ),
    averageWordLength: toNumber(
      getCI(otherClassifications, 'averageWordLength'),
    ),
    uniqueWordCount: toNumber(
      getCI(otherClassifications, 'uniqueWordCount'),
    ),
  }
})

function parseFrequencyWords(
  value: string | FrequencyWord[] | undefined,
): FrequencyWord[] {
  if (!value) {
    return []
  }

  let items: unknown = value

  if (typeof value === 'string') {
    try {
      items = JSON.parse(value)
    } catch {
      return []
    }
  }

  if (!Array.isArray(items)) {
    return []
  }

  // Acepta { Word, Count } o { word, count }
  return items.map((item) => {
    const obj = item as Record<string, unknown>

    return {
      Word: String(getCI(obj, 'word') ?? ''),
      Count: Number(getCI(obj, 'count') ?? 0),
    }
  })
}

const mostFrequentWords = computed(() =>
  parseFrequencyWords(result.value?.mostFrequentWords),
)

const leastFrequentWords = computed(() =>
  parseFrequencyWords(result.value?.leastFrequentWords),
)

const personalPronouns = computed(() =>
  parseArray(result.value?.personalPronouns),
)

const personNames = computed(() =>
  parseArray(result.value?.personNames),
)

const rootNouns = computed(() =>
  parseArray(result.value?.rootNouns),
)

const rootVerbs = computed(() =>
  parseArray(result.value?.rootVerbs),
)

const entities = computed(() => {
  const classifications = parseObject(
    result.value?.otherClassifications,
  )

  const rawEntities = classifications.entities

  if (!rawEntities) {
    return {}
  }

  if (typeof rawEntities === 'string') {
    try {
      return JSON.parse(rawEntities)
    } catch {
      return {}
    }
  }

  return rawEntities as Record<string, unknown>
})

const entityGroups = computed(() => {
  const groups: {
    type: string
    label: string
    values: Entity[]
  }[] = []

  const labels: Record<string, string> = {
    PERSON: 'Personas',
    COUNTRY: 'Países',
    CITY: 'Ciudades',
    ORGANIZATION: 'Organizaciones',
    BRAND: 'Marcas',
    PRODUCT: 'Productos',
    LOCATION: 'Ubicaciones',
  }

  for (const [type, raw] of Object.entries(entities.value)) {
    if (!Array.isArray(raw)) {
      continue
    }

    const values = raw as Entity[]

    groups.push({
      type,
      label: labels[type] ?? type,
      values,
    })
  }

  return groups
})

// Igual que el PDF: 0.9 (no 90.0%)
const formattedLexicalDiversity = computed(() => {
  const value = metrics.value.lexicalDiversity

  if (typeof value !== 'number') {
    return '—'
  }

  return String(Number(value.toFixed(2)))
})

// Igual que el PDF: 6.7
const averageWordLength = computed(() => {
  const value = metrics.value.averageWordLength

  if (typeof value !== 'number') {
    return '—'
  }

  return value.toFixed(1)
})

function statusLabel(status?: string) {
  switch (status) {
    case 'PENDING':
      return 'Pendiente'

    case 'PROCESSING':
      return 'Procesando'

    case 'COMPLETED':
      return 'Completado'

    case 'FAILED':
      return 'Fallido'

    default:
      return status ?? 'Desconocido'
  }
}

function statusClass(status?: string) {
  switch (status) {
    case 'COMPLETED':
      return 'completed'

    case 'PROCESSING':
      return 'processing'

    case 'FAILED':
      return 'failed'

    default:
      return 'pending'
  }
}

function formatDate(value?: string) {
  if (!value) {
    return '—'
  }

  return new Date(value).toLocaleString('es-GT', {
    dateStyle: 'medium',
    timeStyle: 'short',
  })
}

function entityValue(entity: Entity) {
  return (
    entity.text ??
    entity.value ??
    entity.canonicalValue ??
    entity.canonical ??
    '—'
  )
}

function entityCanonical(entity: Entity) {
  const canonical =
    entity.canonicalValue ??
    entity.canonical

  const value =
    entity.text ??
    entity.value

  if (!canonical || canonical === value) {
    return ''
  }

  return canonical
}

async function loadAnalysis() {
  loading.value = true
  error.value = ''

  try {
    const id = String(route.params.id)

    const response = await apiFetch<AnalysisJob>(
      `/Analysis/${id}`,
    )

    job.value = response
    result.value = response.results?.[0] ?? null

    console.log('RESULTADO COMPLETO DEL BACKEND:', response)
    console.log('RESULTADO ANALYSIS:', result.value)
    console.log('FULL RESULT:', result.value?.fullResult)
    console.log('METRICS:', result.value?.metrics)
  } catch (err) {
    error.value = err instanceof Error
      ? err.message
      : 'No fue posible cargar el análisis.'
  } finally {
    loading.value = false
  }
}

// Vista previa del archivo original analizado.
const PREVIEW_MAX_CHARS = 5000
const fileText = ref('')
const fileTruncated = ref(false)
const fileLoading = ref(false)
const fileError = ref('')
const fileExpanded = ref(false)
const fileStats = ref({ lines: 0, words: 0, chars: 0 })

async function loadOriginalFile() {
  fileLoading.value = true
  fileError.value = ''

  try {
    const token = getAuthToken()
    const id = String(route.params.id)

    const response = await fetch(
      `${useRuntimeConfig().public.apiUrl}/Analysis/${id}/file`,
      {
        headers: token ? { Authorization: `Bearer ${token}` } : {},
      },
    )

    if (!response.ok) {
      throw new Error('No fue posible cargar el archivo original.')
    }

    const text = (await response.text()).replace(/^﻿/, '')
    const trimmed = text.trim()

    fileStats.value = {
      lines: text.length ? text.split(/\r\n|\r|\n/).length : 0,
      words: trimmed ? trimmed.split(/\s+/u).length : 0,
      chars: [...text].length,
    }
    fileTruncated.value = text.length > PREVIEW_MAX_CHARS
    fileText.value = fileTruncated.value
      ? text.slice(0, PREVIEW_MAX_CHARS)
      : text
  } catch (err) {
    fileError.value = err instanceof Error
      ? err.message
      : 'No fue posible cargar el archivo original.'
  } finally {
    fileLoading.value = false
  }
}

async function goBack() {
  await navigateTo('/dashboard')
}

async function downloadReport() {
  if (!job.value) {
    return
  }

  error.value = ''

  try {
    const token = localStorage.getItem('token')

    if (!token) {
      throw new Error(
        'La sesión ha expirado. Inicia sesión nuevamente.',
      )
    }

    const response = await fetch(
      `${useRuntimeConfig().public.apiUrl}/Analysis/${job.value.id}/report`,
      {
        headers: {
          Authorization: `Bearer ${token}`,
        },
      },
    )

    if (!response.ok) {
      throw new Error(
        'No fue posible descargar el reporte.',
      )
    }

    const blob = await response.blob()
    const url = window.URL.createObjectURL(blob)

    const link = document.createElement('a')

    link.href = url

    link.download =
      `${job.value.fileName.replace(/\.txt$/i, '')}-reporte.pdf`

    document.body.appendChild(link)

    link.click()

    link.remove()

    window.URL.revokeObjectURL(url)
  } catch (err) {
    error.value = err instanceof Error
      ? err.message
      : 'No fue posible descargar el reporte.'
  }
}

const sendingReport = ref(false)
const sendReportMessage = ref('')

async function sendReport(channel: 'EMAIL' | 'WHATSAPP') {
  if (!job.value) {
    return
  }

  error.value = ''
  sendReportMessage.value = ''
  sendingReport.value = true

  try {
    const result = await apiFetch<{ message: string }>(
      `/Analysis/${job.value.id}/send-report`,
      {
        method: 'POST',
        body: JSON.stringify({ channel }),
      },
    )

    sendReportMessage.value = result.message ?? 'Reporte enviado.'
  } catch (err) {
    error.value = err instanceof Error
      ? err.message
      : 'No fue posible enviar el reporte.'
  } finally {
    sendingReport.value = false
  }
}

function logout() {
  logoutSession()
}

onMounted(() => {
  loadAnalysis()
  loadOriginalFile()
})
</script>

<template>
  <main class="results-page">
    <header class="topbar">
      <div class="brand">
        <div class="brand-mark">
          L
        </div>

        <div>
          <h1>Léxica</h1>
          <p>
            Analiza. Descubre. Comprende.
          </p>
        </div>
      </div>

      <div class="top-actions">
        <button
          class="back-button"
          @click="goBack"
        >
          ← Dashboard
        </button>

        <button
          class="logout"
          @click="logout"
        >
          Cerrar sesión
        </button>
      </div>
    </header>

    <section class="content">
      <div
        v-if="loading"
        class="loading"
      >
        <div class="loading-circle" />

        <strong>
          Cargando análisis...
        </strong>

        <span>
          Estamos preparando tus resultados.
        </span>
      </div>

      <div
        v-else-if="error"
        class="error-screen"
      >
        <div class="error-icon">
          !
        </div>

        <h2>
          No fue posible cargar el análisis
        </h2>

        <p>
          {{ error }}
        </p>

        <button
          class="primary-button"
          @click="goBack"
        >
          Volver al Dashboard
        </button>
      </div>

      <template v-else-if="job">
        <section class="result-header">
          <div>
            <span class="eyebrow">
              RESULTADO DEL ANÁLISIS
            </span>

            <h2>
              {{ job.fileName }}
            </h2>

            <div class="meta">
              <span>
                {{ languageNames[job.language] ?? job.language }}
              </span>

              <span>•</span>

              <span>
                {{ formatDate(job.createdAt) }}
              </span>

              <span>•</span>

              <span
                class="status"
                :class="statusClass(job.status)"
              >
                {{ statusLabel(job.status) }}
              </span>
            </div>
          </div>

          <button
            v-if="job.status === 'COMPLETED'"
            class="pdf-button"
            @click="downloadReport"
          >
            ↓ Descargar PDF
          </button>

          <div
            v-if="job.status === 'COMPLETED'"
            class="send-report-actions"
          >
            <button
              class="pdf-button secondary-button"
              :disabled="sendingReport"
              @click="sendReport('EMAIL')"
            >
              Enviar por correo
            </button>

            <button
              class="pdf-button secondary-button"
              :disabled="sendingReport"
              @click="sendReport('WHATSAPP')"
            >
              Enviar por WhatsApp
            </button>
          </div>

          <p v-if="sendReportMessage" class="send-report-message">
            {{ sendReportMessage }}
          </p>
        </section>

        <div
          v-if="job.status === 'PENDING'"
          class="state-card pending-card"
        >
          <div class="state-icon">
            …
          </div>

          <div>
            <strong>
              Análisis pendiente
            </strong>

            <p>
              Este análisis todavía no ha sido procesado.
            </p>
          </div>
        </div>

        <div
          v-else-if="job.status === 'PROCESSING'"
          class="state-card processing-card"
        >
          <div class="state-icon">
            ↻
          </div>

          <div>
            <strong>
              Procesando documento
            </strong>

            <p>
              El análisis todavía está en proceso.
            </p>
          </div>
        </div>

        <div
          v-else-if="job.status === 'FAILED'"
          class="state-card failed-card"
        >
          <div class="state-icon">
            !
          </div>

          <div>
            <strong>
              El análisis falló
            </strong>

            <p>
              {{ job.errorMessage ?? 'Ocurrió un error durante el análisis.' }}
            </p>
          </div>
        </div>

        <template
          v-else-if="job.status === 'COMPLETED' && result"
        >
          <section class="metrics-grid">
            <article class="metric-card">
              <span>
                Palabras
              </span>

              <strong>
                {{ result.totalWordCount }}
              </strong>

              <small>
                total detectadas
              </small>
            </article>

            <article class="metric-card">
              <span>
                Palabras únicas
              </span>

              <strong>
                {{ metrics.uniqueWordCount ?? '—' }}
              </strong>

              <small>
                vocabulario utilizado
              </small>
            </article>

            <article class="metric-card">
              <span>
                Personas
              </span>

              <strong>
                {{ personNames.length }}
              </strong>

              <small>
                nombres detectados
              </small>
            </article>

            <article class="metric-card">
              <span>
                Verbos
              </span>

              <strong>
                {{ rootVerbs.length }}
              </strong>

              <small>
                formas raíz
              </small>
            </article>
          </section>

          <section class="panel file-panel">
            <div class="panel-title">
              <div>
                <span class="panel-number">
                  TXT
                </span>

                <h3>
                  Vista previa del archivo
                </h3>
              </div>

              <span
                v-if="!fileLoading && !fileError"
                class="file-stats"
              >
                {{ fileStats.lines }} líneas ·
                {{ fileStats.words }} palabras ·
                {{ fileStats.chars }} caracteres
              </span>
            </div>

            <div
              v-if="fileLoading"
              class="empty-small"
            >
              Cargando archivo...
            </div>

            <div
              v-else-if="fileError"
              class="empty-small"
            >
              {{ fileError }}
            </div>

            <div
              v-else-if="!fileText.trim()"
              class="empty-small"
            >
              El archivo está vacío.
            </div>

            <template v-else>
              <pre
                class="file-content"
                :class="{ expanded: fileExpanded }"
                dir="auto"
              >{{ fileText }}</pre>

              <div class="file-footer">
                <span
                  v-if="fileTruncated"
                  class="empty-small"
                >
                  Mostrando los primeros 5,000 caracteres.
                </span>

                <button
                  type="button"
                  class="file-toggle"
                  @click="fileExpanded = !fileExpanded"
                >
                  {{ fileExpanded ? 'Ver menos' : 'Ver más' }}
                </button>
              </div>
            </template>
          </section>

          <section class="two-column">
            <article class="panel">
              <div class="panel-title">
                <div>
                  <span class="panel-number">
                    01
                  </span>

                  <h3>
                    Perfil lingüístico
                  </h3>
                </div>
              </div>

              <div class="stats-list">
                <div>
                  <span>
                    Sustantivos
                  </span>

                  <strong>
                    {{ metrics.nounCount ?? rootNouns.length }}
                  </strong>
                </div>

                <div>
                  <span>
                    Verbos
                  </span>

                  <strong>
                    {{ metrics.verbCount ?? rootVerbs.length }}
                  </strong>
                </div>

                <div>
                  <span>
                    Pronombres
                  </span>

                  <strong>
                    {{ metrics.pronounCount ?? personalPronouns.length }}
                  </strong>
                </div>

                <div>
                  <span>
                    Palabras vacías
                  </span>

                  <strong>
                    {{ metrics.stopWordCount ?? '—' }}
                  </strong>
                </div>

                <div>
                  <span>
                    Longitud media
                  </span>

                  <strong>
                    {{ averageWordLength }}
                  </strong>
                </div>

                <div>
                  <span>
                    Diversidad léxica
                  </span>

                  <strong>
                    {{ formattedLexicalDiversity }}
                  </strong>
                </div>
              </div>
            </article>

            <article class="panel">
              <div class="panel-title">
                <div>
                  <span class="panel-number">
                    02
                  </span>

                  <h3>
                    Personas detectadas
                  </h3>
                </div>

                <span class="count">
                  {{ personNames.length }}
                </span>
              </div>

              <div
                v-if="personNames.length"
                class="tag-list"
              >
                <span
                  v-for="person in personNames"
                  :key="person"
                  class="tag person"
                >
                  {{ person }}
                </span>
              </div>

              <div
                v-else
                class="empty-small"
              >
                No se detectaron personas.
              </div>
            </article>
          </section>

          <section
            v-if="entityGroups.length"
            class="panel entities-panel"
          >
            <div class="panel-title">
              <div>
                <span class="panel-number">
                  03
                </span>

                <h3>
                  Entidades detectadas
                </h3>
              </div>

              <span class="count">
                {{ entityGroups.length }} tipos
              </span>
            </div>

            <div class="entity-grid">
              <article
                v-for="group in entityGroups"
                :key="group.type"
                class="entity-group"
              >
                <div class="entity-heading">
                  <span>
                    {{ group.label }}
                  </span>

                  <strong>
                    {{ group.values.length }}
                  </strong>
                </div>

                <div class="tag-list">
                  <span
                    v-for="(entity, index) in group.values"
                    :key="`${group.type}-${index}`"
                    class="tag"
                  >
                    {{ entityValue(entity) }}

                    <small
                      v-if="entityCanonical(entity)"
                    >
                      {{ entityCanonical(entity) }}
                    </small>
                  </span>
                </div>
              </article>
            </div>
          </section>

          <section class="two-column">
            <article class="panel">
              <div class="panel-title">
                <div>
                  <span class="panel-number">
                    04
                  </span>

                  <h3>
                    Sustantivos raíz
                  </h3>
                </div>

                <span class="count">
                  {{ rootNouns.length }}
                </span>
              </div>

              <div
                v-if="rootNouns.length"
                class="word-list"
              >
                <span
                  v-for="noun in rootNouns"
                  :key="noun"
                >
                  {{ noun }}
                </span>
              </div>

              <div
                v-else
                class="empty-small"
              >
                No se detectaron sustantivos.
              </div>
            </article>

            <article class="panel">
              <div class="panel-title">
                <div>
                  <span class="panel-number">
                    05
                  </span>

                  <h3>
                    Verbos raíz
                  </h3>
                </div>

                <span class="count">
                  {{ rootVerbs.length }}
                </span>
              </div>

              <div
                v-if="rootVerbs.length"
                class="word-list"
              >
                <span
                  v-for="verb in rootVerbs"
                  :key="verb"
                >
                  {{ verb }}
                </span>
              </div>

              <div
                v-else
                class="empty-small"
              >
                No se detectaron verbos.
              </div>
            </article>
          </section>

          <section class="two-column">
            <article class="panel">
              <div class="panel-title">
                <div>
                  <span class="panel-number">
                    06
                  </span>

                  <h3>
                    Palabras más frecuentes
                  </h3>
                </div>

                <span class="count">
                  {{ mostFrequentWords.length }}
                </span>
              </div>

              <div
                v-if="mostFrequentWords.length"
                class="tag-list"
              >
                <span
                  v-for="item in mostFrequentWords"
                  :key="item.Word"
                  class="tag"
                >
                  {{ item.Word }} ({{ item.Count }})
                </span>
              </div>

              <div
                v-else
                class="empty-small"
              >
                No se encontraron datos.
              </div>
            </article>

            <article class="panel">
              <div class="panel-title">
                <div>
                  <span class="panel-number">
                    07
                  </span>

                  <h3>
                    Palabras menos frecuentes
                  </h3>
                </div>

                <span class="count">
                  {{ leastFrequentWords.length }}
                </span>
              </div>

              <div
                v-if="leastFrequentWords.length"
                class="tag-list"
              >
                <span
                  v-for="item in leastFrequentWords"
                  :key="item.Word"
                  class="tag"
                >
                  {{ item.Word }} ({{ item.Count }})
                </span>
              </div>

              <div
                v-else
                class="empty-small"
              >
                No se encontraron datos.
              </div>
            </article>
          </section>

          <section class="panel">
            <div class="panel-title">
              <div>
                <span class="panel-number">
                  08
                </span>

                <h3>
                  Pronombres personales
                </h3>
              </div>

              <span class="count">
                {{ personalPronouns.length }}
              </span>
            </div>

            <div
              v-if="personalPronouns.length"
              class="tag-list"
            >
              <span
                v-for="pronoun in personalPronouns"
                :key="pronoun"
                class="tag"
              >
                {{ pronoun }}
              </span>
            </div>

            <div
              v-else
              class="empty-small"
            >
              No se encontraron datos.
            </div>
          </section>
        </template>
      </template>
    </section>
  </main>
</template>

<style scoped>
.results-page {
  min-height: 100vh;

  background:
    linear-gradient(180deg, rgba(6, 12, 28, 0.55) 0%, rgba(6, 12, 28, 0.78) 100%),
    url('/images/apartado2.png') center / cover no-repeat fixed,
    #07101f;

  color: #eaf1fb;

  font-family:
    'Poppins',
    'Segoe UI',
    system-ui,
    sans-serif;
}

.topbar {
  min-height: 76px;

  padding: 0 5vw;

  display: flex;

  align-items: center;

  justify-content: space-between;

  gap: 20px;

  border-bottom:
    1px solid rgba(73, 151, 208, 0.12);

  background:
    rgba(5, 13, 27, 0.75);

  backdrop-filter: blur(18px);
}

.brand {
  display: flex;

  align-items: center;

  gap: 12px;
}

.brand-mark {
  width: 40px;
  height: 40px;

  display: grid;

  place-items: center;

  border-radius: 12px;

  background:
    linear-gradient(
      135deg,
      rgba(73, 151, 208, 0.3),
      rgba(73, 151, 208, 0.05)
    );

  border:
    1px solid rgba(73, 151, 208, 0.35);

  color: #9cc6ea;

  font-weight: 800;
}

.brand h1 {
  margin: 0;

  font-size: 19px;
}

.brand p {
  margin: 1px 0 0;

  color: #6682a7;

  font-size: 10px;
}

.top-actions {
  display: flex;

  align-items: center;

  gap: 8px;
}

.back-button,
.logout {
  padding: 9px 13px;

  border-radius: 9px;

  cursor: pointer;

  font-size: 11px;
}

.back-button {
  border:
    1px solid rgba(73, 151, 208, 0.25);

  background:
    rgba(73, 151, 208, 0.07);

  color: #a9c5e3;
}

.logout {
  border:
    1px solid rgba(73, 151, 208, 0.15);

  background: transparent;

  color: #7189a9;
}

.content {
  width: min(1100px, 90%);

  margin: 0 auto;

  padding: 55px 0 90px;
}

.loading {
  min-height: 500px;

  display: flex;

  flex-direction: column;

  align-items: center;

  justify-content: center;

  gap: 10px;

  color: #7892b2;
}

.loading-circle {
  width: 36px;
  height: 36px;

  margin-bottom: 10px;

  border:
    3px solid rgba(73, 151, 208, 0.15);

  border-top-color: #4997d0;

  border-radius: 50%;

  animation:
    spin 0.8s linear infinite;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}

.error-screen {
  min-height: 500px;

  display: flex;

  flex-direction: column;

  align-items: center;

  justify-content: center;

  text-align: center;
}

.error-icon {
  width: 52px;
  height: 52px;

  display: grid;

  place-items: center;

  margin-bottom: 18px;

  border-radius: 15px;

  background:
    rgba(248, 113, 113, 0.1);

  color: #fca5a5;

  font-size: 22px;

  font-weight: 800;
}

.error-screen h2 {
  margin: 0 0 8px;

  font-size: 22px;
}

.error-screen p {
  max-width: 500px;

  color: #819ab9;

  font-size: 13px;

  line-height: 1.6;
}

.primary-button {
  margin-top: 20px;

  padding: 11px 18px;

  border: 0;

  border-radius: 9px;

  background: #4997d0;

  color: white;

  font-weight: 700;

  cursor: pointer;
}

.result-header {
  display: flex;

  align-items: flex-end;

  justify-content: space-between;

  gap: 20px;

  margin-bottom: 30px;
}

.eyebrow {
  color: #4997d0;

  font-size: 10px;

  font-weight: 800;

  letter-spacing: 1.8px;
}

.result-header h2 {
  margin: 7px 0 9px;

  font-size: clamp(28px, 4vw, 40px);

  letter-spacing: -1px;

  overflow-wrap: anywhere;
}

.meta {
  display: flex;

  align-items: center;

  flex-wrap: wrap;

  gap: 8px;

  color: #7189a9;

  font-size: 11px;
}

.status {
  padding: 5px 8px;

  border-radius: 20px;

  font-size: 9px;

  font-weight: 800;
}

.status.completed {
  background: rgba(74, 222, 128, 0.1);

  color: #86efac;
}

.status.processing {
  background: rgba(250, 204, 21, 0.1);

  color: #fde047;
}

.status.pending {
  background: rgba(148, 163, 184, 0.1);

  color: #cbd5e1;
}

.status.failed {
  background: rgba(248, 113, 113, 0.1);

  color: #fca5a5;
}

.pdf-button {
  flex-shrink: 0;

  padding: 12px 16px;

  border:
    1px solid rgba(167, 139, 250, 0.3);

  border-radius: 10px;

  background:
    rgba(167, 139, 250, 0.08);

  color: #c4b5fd;

  font-weight: 700;

  cursor: pointer;
}

.send-report-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 8px;
}

.secondary-button {
  font-size: 13px;
  padding: 9px 14px;
  opacity: 0.9;
}

.secondary-button:disabled {
  cursor: not-allowed;
  opacity: 0.5;
}

.send-report-message {
  margin-top: 8px;
  font-size: 12px;
  color: #86efac;
}

.state-card {
  display: flex;

  align-items: center;

  gap: 16px;

  padding: 22px;

  margin-bottom: 24px;

  border-radius: 16px;

  border: 1px solid rgba(73, 151, 208, 0.14);

  background: rgba(10, 21, 40, 0.7);
}

.state-icon {
  width: 44px;
  height: 44px;

  flex-shrink: 0;

  display: grid;

  place-items: center;

  border-radius: 12px;

  background: rgba(73, 151, 208, 0.1);

  color: #78afd7;

  font-size: 20px;

  font-weight: 800;
}

.state-card strong {
  font-size: 14px;
}

.state-card p {
  margin: 4px 0 0;

  color: #7189a9;

  font-size: 12px;
}

.failed-card .state-icon {
  background: rgba(248, 113, 113, 0.1);

  color: #fca5a5;
}

.metrics-grid {
  display: grid;

  grid-template-columns:
    repeat(4, 1fr);

  gap: 12px;

  margin-bottom: 18px;
}

.metric-card {
  padding: 20px;

  border:
    1px solid rgba(73, 151, 208, 0.13);

  border-radius: 15px;

  background:
    rgba(10, 21, 40, 0.72);
}

.metric-card span,
.metric-card small {
  display: block;

  color: #7189a9;

  font-size: 10px;
}

.metric-card strong {
  display: block;

  margin: 8px 0 4px;

  color: #eaf1fb;

  font-size: 30px;

  line-height: 1;
}

.two-column {
  display: grid;

  grid-template-columns: 1fr 1fr;

  gap: 18px;

  margin-bottom: 18px;
}

.panel {
  padding: 23px;

  margin-bottom: 18px;

  border:
    1px solid rgba(73, 151, 208, 0.13);

  border-radius: 16px;

  background:
    rgba(10, 21, 40, 0.72);
}

.two-column .panel {
  margin-bottom: 0;
}

.panel-title {
  display: flex;

  align-items: center;

  justify-content: space-between;

  gap: 10px;

  margin-bottom: 20px;
}

.panel-title > div {
  display: flex;

  align-items: center;

  gap: 10px;
}

.panel-title h3 {
  margin: 0;

  font-size: 14px;
}

.panel-number {
  color: #4997d0;

  font-size: 9px;

  font-weight: 800;

  letter-spacing: 1px;
}

.count {
  min-width: 22px;

  padding: 4px 7px;

  border-radius: 20px;

  background:
    rgba(73, 151, 208, 0.08);

  color: #80b6dc;

  font-size: 9px;

  text-align: center;
}

.stats-list {
  display: flex;

  flex-direction: column;
}

.stats-list > div {
  display: flex;

  align-items: center;

  justify-content: space-between;

  padding: 10px 0;

  border-bottom:
    1px solid rgba(73, 151, 208, 0.07);
}

.stats-list > div:last-child {
  border-bottom: 0;
}

.stats-list span {
  color: #7189a9;

  font-size: 11px;
}

.stats-list strong {
  color: #c9dced;

  font-size: 12px;
}

.tag-list {
  display: flex;

  flex-wrap: wrap;

  gap: 7px;
}

.tag {
  display: inline-flex;

  flex-direction: column;

  gap: 2px;

  padding: 7px 10px;

  border-radius: 8px;

  background:
    rgba(73, 151, 208, 0.08);

  border:
    1px solid rgba(73, 151, 208, 0.12);

  color: #a9c9e5;

  font-size: 11px;
}

.tag.person {
  background:
    rgba(139, 92, 246, 0.08);

  border-color:
    rgba(139, 92, 246, 0.16);

  color: #c4b5fd;
}

.tag small {
  color: #6682a7;

  font-size: 8px;
}

.empty-small {
  color: #627c9f;

  font-size: 11px;
}

.file-stats {
  color: #627c9f;

  font-size: 11px;
}

.file-content {
  margin: 0;

  max-height: 180px;

  overflow: auto;

  padding: 14px 16px;

  border: 1px solid rgba(73, 151, 208, 0.13);

  border-radius: 12px;

  background: rgba(5, 13, 27, 0.6);

  color: #cfdcef;

  font-family: ui-monospace, 'Cascadia Code', Consolas, monospace;

  font-size: 12.5px;

  line-height: 1.6;

  white-space: pre-wrap;

  word-break: break-word;

  unicode-bidi: plaintext;
}

.file-content.expanded {
  max-height: 520px;
}

.file-footer {
  display: flex;

  flex-wrap: wrap;

  align-items: center;

  justify-content: space-between;

  gap: 8px;

  margin-top: 10px;
}

.file-toggle {
  margin-left: auto;

  padding: 6px 12px;

  border: 1px solid rgba(73, 151, 208, 0.3);

  border-radius: 10px;

  background: transparent;

  color: #80b6dc;

  cursor: pointer;

  font-size: 12px;
}

.file-toggle:hover {
  background: rgba(73, 151, 208, 0.08);
}

.entities-panel {
  margin-bottom: 18px;
}

.entity-grid {
  display: grid;

  grid-template-columns:
    repeat(2, 1fr);

  gap: 18px;
}

.entity-group {
  padding: 16px;

  border-radius: 12px;

  background:
    rgba(73, 151, 208, 0.035);

  border:
    1px solid rgba(73, 151, 208, 0.08);
}

.entity-heading {
  display: flex;

  justify-content: space-between;

  align-items: center;

  margin-bottom: 12px;
}

.entity-heading span {
  color: #9ab6d3;

  font-size: 11px;

  font-weight: 700;
}

.entity-heading strong {
  color: #6682a7;

  font-size: 10px;
}

.word-list {
  display: flex;

  flex-wrap: wrap;

  gap: 7px;
}

.word-list span {
  padding: 7px 10px;

  border-radius: 8px;

  background:
    rgba(73, 151, 208, 0.07);

  color: #a9c9e5;

  font-size: 11px;
}

.frequency-list {
  display: flex;

  flex-direction: column;

  gap: 7px;
}

.frequency-list > div {
  display: flex;

  align-items: center;

  gap: 10px;

  padding: 9px;

  border-radius: 8px;

  background:
    rgba(73, 151, 208, 0.035);
}

.rank {
  width: 22px;
  height: 22px;

  display: grid;

  place-items: center;

  border-radius: 6px;

  background:
    rgba(73, 151, 208, 0.08);

  color: #6682a7;

  font-size: 9px;

  font-weight: 800;
}

.frequency-list strong {
  color: #b8cee3;

  font-size: 11px;
}

.frequency-count {
  margin-left: auto;

  min-width: 22px;

  padding: 4px 7px;

  border-radius: 6px;

  background:
    rgba(73, 151, 208, 0.08);

  color: #7faed2;

  font-size: 9px;

  font-weight: 700;

  text-align: center;
}

@media (max-width: 800px) {
  .metrics-grid {
    grid-template-columns:
      repeat(2, 1fr);
  }

  .two-column,
  .entity-grid {
    grid-template-columns: 1fr;
  }

  .result-header {
    align-items: flex-start;

    flex-direction: column;
  }

  .pdf-button {
    width: 100%;
  }
}

@media (max-width: 550px) {
  .topbar {
    padding: 12px 4%;

    align-items: flex-start;

    flex-direction: column;
  }

  .top-actions {
    width: 100%;
  }

  .back-button,
  .logout {
    flex: 1;
  }

  .content {
    width: 92%;

    padding-top: 35px;
  }

  .metrics-grid {
    grid-template-columns: 1fr;
  }

  .panel {
    padding: 18px;
  }
}
</style>