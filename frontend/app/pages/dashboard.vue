<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { apiFetch, logoutSession } from '~/services/api'

definePageMeta({ middleware: 'auth' })

const isAdmin = ref(false)
const isStaff = ref(false)
const myAvatarUrl = ref('')

interface AnalysisJob {
  id: string
  fileName: string
  language: string
  status: string
  reportRequested: boolean
  uploadedAt: string
  processedAt?: string | null
  createdAt: string
  updatedAt?: string
}

const selectedFile = ref<File | null>(null)
const language = ref('ES')
const reportRequested = ref(true)

const analyses = ref<AnalysisJob[]>([])
const loading = ref(false)
const loadingHistory = ref(false)
const error = ref('')
const success = ref('')

const retryingIds = ref<string[]>([])

// Al terminar un análisis se pregunta si se quiere recibir el reporte PDF por
// el método de notificación del usuario (correo, WhatsApp o ambos).
const notificationMethod = ref<'EMAIL' | 'WHATSAPP' | 'BOTH'>('EMAIL')
const reportPrompt = ref<{ id: string, fileName: string } | null>(null)
const sendingReport = ref(false)
const reportPromptMessage = ref('')
const reportPromptError = ref('')

function openReportPrompt(id: string, fileName: string) {
  reportPrompt.value = { id, fileName }
  reportPromptMessage.value = ''
  reportPromptError.value = ''
}

async function sendReport(channel: 'EMAIL' | 'WHATSAPP') {
  if (!reportPrompt.value) return

  sendingReport.value = true
  reportPromptError.value = ''

  try {
    const result = await apiFetch<{ message: string }>(`/Analysis/${reportPrompt.value.id}/send-report`, {
      method: 'POST',
      body: JSON.stringify({ channel }),
    })
    reportPromptMessage.value = result.message ?? 'Reporte enviado.'
    await loadHistory()
  } catch (err) {
    reportPromptError.value = err instanceof Error ? err.message : 'No fue posible enviar el reporte.'
  } finally {
    sendingReport.value = false
  }
}

const fileInput = ref<HTMLInputElement | null>(null)

// Vista previa del archivo seleccionado (antes de enviarlo a analizar).
const PREVIEW_MAX_CHARS = 5000
const previewText = ref('')
const previewTruncated = ref(false)
const previewStats = ref({ lines: 0, words: 0, chars: 0 })
const previewLoading = ref(false)
const previewError = ref('')

async function buildPreview(file: File) {
  previewLoading.value = true
  previewError.value = ''
  previewText.value = ''

  try {
    const text = (await file.text()).replace(/^﻿/, '')

    // Si el usuario cambió de archivo mientras se leía, se descarta.
    if (selectedFile.value !== file) return

    const trimmed = text.trim()
    previewStats.value = {
      lines: text.length ? text.split(/\r\n|\r|\n/).length : 0,
      words: trimmed ? trimmed.split(/\s+/u).length : 0,
      chars: [...text].length,
    }
    previewTruncated.value = text.length > PREVIEW_MAX_CHARS
    previewText.value = previewTruncated.value
      ? text.slice(0, PREVIEW_MAX_CHARS)
      : text
  } catch {
    previewError.value = 'No fue posible leer el archivo para la vista previa.'
  } finally {
    previewLoading.value = false
  }
}

function resetPreview() {
  previewText.value = ''
  previewTruncated.value = false
  previewStats.value = { lines: 0, words: 0, chars: 0 }
  previewError.value = ''
  previewLoading.value = false
}

const languageNames: Record<string, string> = {
  ES: 'Español',
  EN: 'English',
  RU: 'Русский',
  ZH: '中文',
  AR: 'العربية',
}

function selectFile() {
  fileInput.value?.click()
}

function handleFile(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]

  if (!file) {
    selectedFile.value = null
    resetPreview()
    return
  }

  if (!file.name.toLowerCase().endsWith('.txt')) {
    error.value = 'Solo se permiten archivos .txt.'
    selectedFile.value = null
    resetPreview()
    return
  }

  if (file.size > 5 * 1024 * 1024) {
    error.value = 'El archivo no puede superar los 5 MB.'
    selectedFile.value = null
    resetPreview()
    return
  }

  error.value = ''
  success.value = ''
  selectedFile.value = file
  buildPreview(file)
}

function clearFile() {
  selectedFile.value = null
  resetPreview()

  if (fileInput.value) {
    fileInput.value.value = ''
  }
}

async function loadHistory() {
  loadingHistory.value = true

  try {
    analyses.value = await apiFetch<AnalysisJob[]>('/Analysis')
  } catch (err) {
    error.value = err instanceof Error
      ? err.message
      : 'No fue posible cargar el historial.'
  } finally {
    loadingHistory.value = false
  }
}

async function createAnalysis() {
  error.value = ''
  success.value = ''

  if (!selectedFile.value) {
    error.value = 'Selecciona un archivo .txt.'
    return
  }

  loading.value = true

  try {
    const formData = new FormData()

    formData.append('Language', language.value)
    formData.append('ReportRequested', String(reportRequested.value))
    formData.append('file', selectedFile.value)

    const job = await apiFetch<AnalysisJob>('/Analysis', {
      method: 'POST',
      body: formData,
    })

    success.value = 'Archivo cargado correctamente. Procesando...'

    await processAnalysis(job.id)

    clearFile()

    // Sin ventana emergente: se abre directo el resultado, donde ya estan los
    // botones "Descargar PDF", "Enviar por correo" y "Enviar por WhatsApp".
    await openResult(job.id)
  } catch (err) {
    console.error('Error en createAnalysis:', err)

    error.value = err instanceof Error
      ? err.message
      : 'No fue posible realizar el análisis.'
  } finally {
    loading.value = false
  }
}

async function processAnalysis(id: string) {
  await apiFetch(`/Analysis/${id}/process`, {
    method: 'POST',
  })

  success.value = 'Análisis completado correctamente.'
}

async function openResult(id: string) {
  await navigateTo(`/analysis/${id}`)
}

async function deleteAnalysis(id: string) {
  const confirmed = window.confirm(
    '¿Seguro que deseas eliminar este análisis? Esta acción no se puede deshacer.',
  )

  if (!confirmed) {
    return
  }

  error.value = ''
  success.value = ''

  try {
    await apiFetch(`/Analysis/${id}`, {
      method: 'DELETE',
    })

    success.value = 'Análisis eliminado correctamente.'

    await loadHistory()
  } catch (err) {
    error.value = err instanceof Error
      ? err.message
      : 'No fue posible eliminar el análisis.'
  }
}

async function retryAnalysis(id: string) {
  if (retryingIds.value.includes(id)) {
    return
  }

  error.value = ''
  success.value = ''

  retryingIds.value.push(id)

  try {
    await apiFetch(`/Analysis/${id}/retry`, {
      method: 'POST',
    })

    success.value = 'Reintentando análisis...'

    await apiFetch(`/Analysis/${id}/process`, {
      method: 'POST',
    })

    success.value = 'Análisis completado correctamente.'
  } catch (err) {
    success.value = ''

    error.value = err instanceof Error
      ? err.message
      : 'No fue posible reintentar el análisis.'
  } finally {
    retryingIds.value =
      retryingIds.value.filter(x => x !== id)

    await loadHistory()
  }
}

async function downloadReport(id: string, fileName: string) {
  error.value = ''

  try {
    const token = localStorage.getItem('token')

    const response = await fetch(
      `${useRuntimeConfig().public.apiUrl}/Analysis/${id}/report`,
      {
        headers: {
          Authorization: `Bearer ${token}`,
        },
      },
    )

    if (!response.ok) {
      throw new Error('No fue posible descargar el reporte.')
    }

    const blob = await response.blob()
    const url = window.URL.createObjectURL(blob)

    const link = document.createElement('a')
    link.href = url
    link.download = `${fileName.replace(/\.txt$/i, '')}-reporte.pdf`

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

function statusLabel(status: string) {
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
      return status
  }
}

function statusClass(status: string) {
  switch (status) {
    case 'COMPLETED':
      return 'status completed'
    case 'PROCESSING':
      return 'status processing'
    case 'FAILED':
      return 'status failed'
    default:
      return 'status pending'
  }
}

function formatDate(value: string) {
  return new Date(value).toLocaleString('es-GT', {
    dateStyle: 'medium',
    timeStyle: 'short',
  })
}

function logout() {
  logoutSession()
}

onMounted(() => {
  isAdmin.value = getAuthRole() === 'ADMIN'
  isStaff.value = isAdmin.value || getAuthRole() === 'SUPERVISOR'
  loadHistory()
  apiFetch<{ notificationMethod: 'EMAIL' | 'WHATSAPP' | 'BOTH' }>('/Profile/me')
    .then((me) => { notificationMethod.value = me.notificationMethod })
    .catch(() => {})
  fetchAuthedObjectUrl('/Profile/photo/modified').then((url) => {
    myAvatarUrl.value = url ?? ''
  })
})
</script>

<template>
  <main class="dashboard">
    <header class="topbar">
      <div class="brand">
        <div class="brand-mark">L</div>

        <div>
          <h1>Léxica</h1>
          <p>Analiza. Descubre. Comprende.</p>
        </div>
      </div>

      <div class="topbar-actions">
        <NuxtLink to="/perfil" class="status-avatar-link">
          <img v-if="myAvatarUrl" :src="myAvatarUrl" alt="Mi foto de perfil" class="status-avatar">
        </NuxtLink>

        <NuxtLink to="/perfil" class="logout">Mi perfil</NuxtLink>
        <NuxtLink v-if="isStaff" to="/supervisor" class="logout">Supervisión</NuxtLink>
        <NuxtLink v-if="isStaff" to="/users" class="logout">Usuarios</NuxtLink>
        <NuxtLink v-if="isAdmin" to="/auditoria" class="logout">Auditoría</NuxtLink>
        <NuxtLink v-if="isAdmin" to="/solicitudes" class="logout">Solicitudes</NuxtLink>
        <NuxtLink v-if="isAdmin" to="/parametros" class="logout">Parámetros</NuxtLink>

        <button class="logout" @click="logout">
          Cerrar sesión
        </button>
      </div>
    </header>

    <section class="content">
      <div class="hero">
        <div>
          <span class="eyebrow">PLATAFORMA DE ANÁLISIS</span>

          <h2>
            Analiza tu texto
          </h2>

          <p>
            Carga un documento y descubre sus características
            lingüísticas, entidades y estadísticas.
          </p>
        </div>
      </div>

      <section class="upload-card">
        <div class="section-title">
          <div>
            <span class="step">01</span>
            <h3>Nuevo análisis</h3>
          </div>

          <span class="format">
            TXT · Máx. 5 MB
          </span>
        </div>

        <div
          class="dropzone"
          :class="{ selected: selectedFile }"
          @click="selectFile"
        >
          <input
            ref="fileInput"
            type="file"
            accept=".txt,text/plain"
            hidden
            @change="handleFile"
          >

          <template v-if="!selectedFile">
            <div class="upload-icon">
              ↑
            </div>

            <strong>
              Selecciona tu archivo
            </strong>

            <span>
              Haz clic aquí para buscar un archivo .txt
            </span>
          </template>

          <template v-else>
            <div class="file-icon">
              TXT
            </div>

            <strong>
              {{ selectedFile.name }}
            </strong>

            <span>
              {{ (selectedFile.size / 1024).toFixed(1) }} KB
            </span>

            <button
              type="button"
              class="remove-file"
              @click.stop="clearFile"
            >
              Quitar archivo
            </button>
          </template>
        </div>

        <div
          v-if="selectedFile"
          class="file-preview"
        >
          <div class="preview-header">
            <strong>Vista previa</strong>

            <span v-if="!previewLoading && !previewError">
              {{ previewStats.lines }} líneas ·
              {{ previewStats.words }} palabras ·
              {{ previewStats.chars }} caracteres
            </span>
          </div>

          <p
            v-if="previewLoading"
            class="preview-status"
          >
            Leyendo archivo...
          </p>

          <p
            v-else-if="previewError"
            class="preview-status preview-error"
          >
            {{ previewError }}
          </p>

          <p
            v-else-if="!previewText.trim()"
            class="preview-status"
          >
            El archivo está vacío.
          </p>

          <pre
            v-else
            class="preview-content"
            dir="auto"
          >{{ previewText }}</pre>

          <p
            v-if="previewTruncated"
            class="preview-status"
          >
            Mostrando los primeros 5,000 caracteres. El archivo completo se enviará al análisis.
          </p>
        </div>

        <div class="options">
          <div class="option">
            <label for="language">
              Idioma
            </label>

            <select
              id="language"
              v-model="language"
              :disabled="loading"
            >
              <option value="ES">
                Español
              </option>

              <option value="EN">
                English
              </option>

              <option value="RU">
                Русский
              </option>

              <option value="ZH">
                中文
              </option>

              <option value="AR">
                العربية
              </option>
            </select>
          </div>

          <label class="pdf-option">
            <input
              v-model="reportRequested"
              type="checkbox"
              :disabled="loading"
            >

            <span>
              <strong>Generar reporte PDF</strong>
              <small>
                Crear un reporte descargable al finalizar
              </small>
            </span>
          </label>
        </div>

        <div v-if="error" class="message error">
          {{ error }}
        </div>

        <div v-if="success" class="message success">
          {{ success }}
        </div>

        <button
          class="analyze-button"
          :disabled="loading || !selectedFile"
          @click="createAnalysis"
        >
          <span v-if="loading">
            Procesando...
          </span>

          <span v-else>
            Procesar
          </span>
        </button>
      </section>

      <section class="history">
        <div class="history-header">
          <div>
            <span class="step">02</span>

            <h3>
              Historial de análisis
            </h3>
          </div>

          <button
            class="refresh"
            :disabled="loadingHistory"
            @click="loadHistory"
          >
            ↻ Actualizar
          </button>
        </div>

        <div v-if="loadingHistory" class="empty">
          Cargando historial...
        </div>

        <div
          v-else-if="analyses.length === 0"
          class="empty"
        >
          <div class="empty-icon">
            ◌
          </div>

          <strong>
            Aún no tienes análisis
          </strong>

          <span>
            Carga tu primer archivo para comenzar.
          </span>
        </div>

        <div v-else class="analysis-list">
          <article
            v-for="analysis in analyses"
            :key="analysis.id"
            class="analysis-item"
          >
            <div class="analysis-file">
              <div class="file-small">
                TXT
              </div>

              <div>
                <strong>
                  {{ analysis.fileName }}
                </strong>

                <span>
                  {{ languageNames[analysis.language] ?? analysis.language }}
                  ·
                  {{ formatDate(analysis.createdAt) }}
                </span>
              </div>
            </div>

            <div class="analysis-status">
              <span :class="statusClass(analysis.status)">
                {{ statusLabel(analysis.status) }}
              </span>
            </div>

            <div class="analysis-actions">
              <button
                v-if="analysis.status === 'COMPLETED'"
                type="button"
                class="view-button"
                @click="openResult(analysis.id)"
              >
                Ver resultado
              </button>

              <button
                v-if="
                  analysis.status === 'COMPLETED' &&
                  analysis.reportRequested
                "
                type="button"
                class="pdf-button"
                @click="
                  downloadReport(
                    analysis.id,
                    analysis.fileName,
                  )
                "
              >
                PDF
              </button>

              <button
                v-if="
                  analysis.status === 'PENDING' ||
                  analysis.status === 'FAILED'
                "
                type="button"
                class="retry-button"
                :disabled="retryingIds.includes(analysis.id)"
                @click="retryAnalysis(analysis.id)"
              >
                {{
                  retryingIds.includes(analysis.id)
                    ? 'Reintentando...'
                    : 'Reintentar'
                }}
              </button>

              <span
                v-if="analysis.status === 'PROCESSING'"
                class="processing-label"
              >
                Procesando...
              </span>

              <button
                type="button"
                class="danger-button"
                :disabled="
                  analysis.status === 'PROCESSING' ||
                  retryingIds.includes(analysis.id)
                "
                @click="deleteAnalysis(analysis.id)"
              >
                Eliminar
              </button>
            </div>
          </article>
        </div>
      </section>
    </section>
    <div v-if="reportPrompt" class="report-backdrop" @click.self="reportPrompt = null">
      <section class="report-modal" role="dialog" aria-label="Recibir reporte">
        <h2>Análisis completado</h2>
        <p>
          <strong>{{ reportPrompt.fileName }}</strong> ya fue procesado.
          ¿Deseas recibir el reporte en PDF?
        </p>

        <div v-if="reportPromptMessage" class="report-ok">{{ reportPromptMessage }}</div>
        <div v-if="reportPromptError" class="report-err">{{ reportPromptError }}</div>

        <div class="report-actions">
          <button
            v-if="notificationMethod !== 'WHATSAPP'"
            type="button"
            class="report-primary"
            :disabled="sendingReport"
            @click="sendReport('EMAIL')"
          >
            Enviar a mi correo
          </button>
          <button
            v-if="notificationMethod !== 'EMAIL'"
            type="button"
            class="report-primary"
            :disabled="sendingReport"
            @click="sendReport('WHATSAPP')"
          >
            Enviar por WhatsApp
          </button>
          <button type="button" class="report-ghost" @click="openResult(reportPrompt.id)">Ver resultados</button>
          <button type="button" class="report-ghost" @click="reportPrompt = null">
            {{ reportPromptMessage ? 'Cerrar' : 'No, gracias' }}
          </button>
        </div>
      </section>
    </div>
  </main>
</template>

<style scoped>
.dashboard {
  min-height: 100vh;
  background:
    linear-gradient(180deg, rgba(6, 12, 28, 0.55) 0%, rgba(6, 12, 28, 0.78) 100%),
    url('/images/apartado1.png') center / cover no-repeat fixed,
    #07101f;

  color: #eaf1fb;

  font-family:
    'Poppins',
    'Segoe UI',
    system-ui,
    sans-serif;
}

.topbar {
  height: 76px;
  padding: 0 5vw;

  display: flex;
  align-items: center;
  justify-content: space-between;

  border-bottom:
    1px solid rgba(73, 151, 208, 0.12);

  background: rgba(5, 13, 27, 0.75);
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

  font-size: 10px;

  color: #6682a7;
}

.logout {
  padding: 9px 16px;

  border:
    1px solid rgba(73, 151, 208, 0.25);

  border-radius: 9px;

  background: rgba(73, 151, 208, 0.06);

  color: #a9c5e3;

  cursor: pointer;
}

.topbar-actions {
  display: flex;
  align-items: center;
  gap: 10px;
}

.status-avatar-link {
  display: flex;
}

.status-avatar {
  width: 34px;
  height: 34px;
  border-radius: 50%;
  object-fit: cover;
  border: 2px solid rgba(73, 151, 208, 0.4);
}

a.logout {
  text-decoration: none;
  font-size: 13.5px;
}

.logout:hover {
  background: rgba(73, 151, 208, 0.14);
}

.content {
  width: min(1050px, 90%);

  margin: 0 auto;

  padding: 60px 0 90px;
}

.hero {
  margin-bottom: 34px;
}

.eyebrow {
  color: #4997d0;

  font-size: 11px;

  font-weight: 700;

  letter-spacing: 1.8px;
}

.hero h2 {
  margin: 8px 0 10px;

  font-size: clamp(32px, 5vw, 48px);

  letter-spacing: -1.5px;
}

.hero p {
  max-width: 620px;

  margin: 0;

  color: #819ab9;

  line-height: 1.7;
}

.upload-card,
.history {
  padding: 28px;

  border:
    1px solid rgba(73, 151, 208, 0.16);

  border-radius: 20px;

  background: rgba(10, 21, 40, 0.75);

  box-shadow:
    0 25px 70px rgba(0, 0, 0, 0.22);

  backdrop-filter: blur(16px);
}

.section-title,
.history-header {
  display: flex;

  align-items: center;

  justify-content: space-between;

  margin-bottom: 22px;
}

.section-title > div,
.history-header > div {
  display: flex;

  align-items: center;

  gap: 12px;
}

.step {
  color: #4997d0;

  font-size: 11px;

  font-weight: 800;

  letter-spacing: 1px;
}

h3 {
  margin: 0;

  font-size: 18px;
}

.format {
  color: #627c9f;

  font-size: 11px;
}

.dropzone {
  min-height: 190px;

  display: flex;

  flex-direction: column;

  align-items: center;

  justify-content: center;

  gap: 8px;

  border:
    1px dashed rgba(73, 151, 208, 0.35);

  border-radius: 16px;

  background:
    rgba(73, 151, 208, 0.035);

  cursor: pointer;

  transition:
    border-color 0.2s,
    background 0.2s,
    transform 0.2s;
}

.dropzone:hover {
  border-color: rgba(73, 151, 208, 0.7);

  background:
    rgba(73, 151, 208, 0.07);

  transform: translateY(-1px);
}

.dropzone.selected {
  border-style: solid;

  background:
    rgba(73, 151, 208, 0.07);
}

.upload-icon,
.file-icon {
  width: 48px;
  height: 48px;

  display: grid;
  place-items: center;

  margin-bottom: 8px;

  border-radius: 13px;

  background: rgba(73, 151, 208, 0.1);

  color: #6faad8;

  font-weight: 800;
}

.upload-icon {
  font-size: 24px;
}

.file-icon {
  font-size: 11px;
}

.dropzone strong {
  font-size: 14px;
}

.dropzone > span {
  color: #647e9f;

  font-size: 12px;
}

.file-preview {
  margin-top: 14px;

  border: 1px solid rgba(73, 151, 208, 0.25);

  border-radius: 14px;

  background: rgba(73, 151, 208, 0.04);

  overflow: hidden;
}

.preview-header {
  display: flex;

  flex-wrap: wrap;

  align-items: center;

  justify-content: space-between;

  gap: 6px 12px;

  padding: 10px 14px;

  border-bottom: 1px solid rgba(73, 151, 208, 0.18);

  font-size: 13px;
}

.preview-header span {
  color: #627c9f;

  font-size: 11px;
}

.preview-content {
  margin: 0;

  max-height: 260px;

  overflow: auto;

  padding: 12px 14px;

  font-family: ui-monospace, 'Cascadia Code', Consolas, monospace;

  font-size: 12.5px;

  line-height: 1.55;

  white-space: pre-wrap;

  word-break: break-word;

  unicode-bidi: plaintext;
}

.preview-status {
  margin: 0;

  padding: 10px 14px;

  color: #627c9f;

  font-size: 12px;
}

.preview-error {
  color: #fca5a5;
}

.remove-file {
  margin-top: 8px;

  border: 0;

  background: transparent;

  color: #fca5a5;

  cursor: pointer;

  font-size: 12px;
}

.options {
  display: grid;

  grid-template-columns: 1fr 1fr;

  gap: 18px;

  margin-top: 22px;
}

.option label {
  display: block;

  margin-bottom: 8px;

  color: #86a5c8;

  font-size: 12px;

  font-weight: 600;
}

select {
  width: 100%;

  box-sizing: border-box;

  padding: 12px;

  border:
    1px solid rgba(73, 151, 208, 0.25);

  border-radius: 10px;

  background: #0a162a;

  color: #eaf1fb;

  outline: none;
}

.pdf-option {
  display: flex;

  align-items: center;

  gap: 10px;

  padding: 10px 12px;

  border:
    1px solid rgba(73, 151, 208, 0.12);

  border-radius: 10px;
}

.pdf-option input {
  accent-color: #4997d0;
}

.pdf-option span {
  display: flex;

  flex-direction: column;

  gap: 3px;
}

.pdf-option strong {
  font-size: 12px;
}

.pdf-option small {
  color: #647e9f;

  font-size: 10px;
}

.message {
  margin-top: 18px;

  padding: 11px 13px;

  border-radius: 10px;

  font-size: 12px;
}

.message.error {
  border:
    1px solid rgba(248, 113, 113, 0.3);

  background: rgba(127, 29, 29, 0.15);

  color: #fca5a5;
}

.message.success {
  border:
    1px solid rgba(74, 222, 128, 0.2);

  background: rgba(22, 101, 52, 0.14);

  color: #86efac;
}

.analyze-button {
  width: 100%;

  margin-top: 20px;

  padding: 14px;

  border: 0;

  border-radius: 11px;

  background:
    linear-gradient(
      135deg,
      #2f6fb8,
      #4997d0
    );

  color: white;

  font-size: 14px;

  font-weight: 700;

  cursor: pointer;

  transition:
    transform 0.2s,
    box-shadow 0.2s,
    opacity 0.2s;
}

.analyze-button:hover:not(:disabled) {
  transform: translateY(-1px);

  box-shadow:
    0 12px 30px rgba(73, 151, 208, 0.3);
}

.analyze-button:disabled {
  opacity: 0.45;

  cursor: not-allowed;
}

.history {
  margin-top: 28px;
}

.refresh {
  border: 0;

  background: transparent;

  color: #6faad8;

  cursor: pointer;

  font-size: 12px;
}

.empty {
  min-height: 180px;

  display: flex;

  flex-direction: column;

  align-items: center;

  justify-content: center;

  gap: 7px;

  color: #7189a9;

  text-align: center;
}

.empty strong {
  color: #9db6d4;

  font-size: 14px;
}

.empty span {
  font-size: 12px;
}

.empty-icon {
  margin-bottom: 5px;

  color: #477ca6;

  font-size: 30px;
}

.analysis-list {
  display: flex;

  flex-direction: column;

  gap: 10px;
}

.analysis-item {
  display: grid;

  grid-template-columns: 1.5fr 0.7fr auto;

  align-items: center;

  gap: 16px;

  padding: 14px;

  border:
    1px solid rgba(73, 151, 208, 0.1);

  border-radius: 12px;

  background: rgba(73, 151, 208, 0.025);
}

.analysis-file {
  display: flex;

  align-items: center;

  gap: 12px;

  min-width: 0;
}

.file-small {
  width: 36px;
  height: 36px;

  flex-shrink: 0;

  display: grid;
  place-items: center;

  border-radius: 9px;

  background: rgba(73, 151, 208, 0.1);

  color: #6faad8;

  font-size: 9px;

  font-weight: 800;
}

.analysis-file > div:last-child {
  display: flex;

  flex-direction: column;

  gap: 4px;

  min-width: 0;
}

.analysis-file strong {
  overflow: hidden;

  text-overflow: ellipsis;

  white-space: nowrap;

  font-size: 13px;
}

.analysis-file span {
  color: #627c9f;

  font-size: 10px;
}

.status {
  display: inline-block;

  width: fit-content;

  padding: 5px 8px;

  border-radius: 20px;

  font-size: 10px;

  font-weight: 700;
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

.analysis-actions {
  display: flex;

  gap: 7px;
}

.retry-button {
  border: 1px solid rgba(250, 204, 21, 0.3);

  background: rgba(250, 204, 21, 0.08);

  color: #fde047;
}

.retry-button:hover:not(:disabled) {
  background: rgba(250, 204, 21, 0.16);
}

.processing-label {
  display: inline-flex;

  align-items: center;

  padding: 8px 10px;

  color: #fde047;

  font-size: 10px;

  font-weight: 700;
}

.retry-button:disabled,
.danger-button:disabled {
  opacity: 0.45;

  cursor: not-allowed;
}

.view-button,
.pdf-button,
.retry-button,
.danger-button {
  padding: 8px 10px;

  border-radius: 8px;

  cursor: pointer;

  font-size: 10px;

  font-weight: 700;
}

.view-button {
  border:
    1px solid rgba(73, 151, 208, 0.3);

  background: rgba(73, 151, 208, 0.08);

  color: #9cc6ea;
}

.pdf-button {
  border:
    1px solid rgba(167, 139, 250, 0.25);

  background: rgba(167, 139, 250, 0.07);

  color: #c4b5fd;
}

.danger-button {
  border: 1px solid rgba(239, 68, 68, 0.35);

  background: rgba(239, 68, 68, 0.08);

  color: #f87171;

  transition: 0.2s ease;
}

.danger-button:hover {
  background: rgba(239, 68, 68, 0.16);

  border-color: rgba(239, 68, 68, 0.55);
}

@media (max-width: 760px) {
  .content {
    width: 92%;
    padding-top: 40px;
  }

  .options {
    grid-template-columns: 1fr;
  }

  .analysis-item {
    grid-template-columns: 1fr;
  }

  .analysis-actions {
    justify-content: flex-start;
  }

  .topbar {
    padding: 0 4%;
  }
}

@media (max-width: 480px) {
  .upload-card,
  .history {
    padding: 20px;
  }

  .format {
    display: none;
  }

  .section-title,
  .history-header {
    align-items: flex-start;
  }
}

.report-backdrop {
  position: fixed; inset: 0; z-index: 60; display: grid; place-items: center; padding: 16px;
  background: rgba(2, 6, 15, 0.7); backdrop-filter: blur(3px);
}
.report-modal {
  width: min(460px, 100%); padding: 24px; border-radius: 16px; background: #0a1528;
  border: 1px solid rgba(73, 151, 208, 0.25); color: #eaf1fb; box-shadow: 0 25px 70px rgba(0, 0, 0, 0.45);
}
.report-modal h2 { margin: 0 0 8px; font-size: 18px; }
.report-modal p { margin: 0 0 14px; color: #9fb4cf; font-size: 13px; line-height: 1.5; }
.report-actions { display: flex; flex-wrap: wrap; gap: 8px; }
.report-actions button { border: 0; border-radius: 10px; padding: 10px 14px; font: inherit; font-size: 13px; font-weight: 600; cursor: pointer; }
.report-actions button:disabled { opacity: 0.45; cursor: not-allowed; }
.report-primary { background: linear-gradient(135deg, #2f6fb8, #4997d0); color: #fff; }
.report-ghost { background: rgba(73, 151, 208, 0.1); color: #cddcee; }
.report-ok, .report-err { margin-bottom: 12px; padding: 10px 12px; border-radius: 10px; font-size: 12.5px; }
.report-ok { background: rgba(34, 197, 94, 0.12); color: #86efac; border: 1px solid rgba(34, 197, 94, 0.25); }
.report-err { background: rgba(127, 29, 29, 0.15); color: #fca5a5; border: 1px solid rgba(248, 113, 113, 0.3); }
</style>