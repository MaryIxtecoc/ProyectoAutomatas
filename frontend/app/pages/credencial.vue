<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import { apiFetch } from '~/services/api'

// Sandbox de diseño de la credencial: SOLO apariencia. Nickname, email, rol,
// foto y QR los pone el backend con los datos reales del usuario.
definePageMeta({ middleware: 'auth' })

interface Decoration {
  source: 'GLOBAL' | 'USER'
  file: string
  x: number
  y: number
  size: number
  rotation: number
}

interface Design {
  template: string
  backgroundColor: string
  accentColor: string
  textColor: string
  photoStyle: string
  qrStyle: string
  decorations: Decoration[]
}

interface Profile {
  nickname: string
  email: string
  roleName: string
  credentialIssuedAt?: string | null
}

const templateLabels: Record<string, string> = { CLASSIC: 'Clásica', LATERAL: 'Banda lateral', MINIMAL: 'Minimalista' }
const photoLabels: Record<string, string> = { SQUARE: 'Cuadrada', ROUNDED: 'Redondeada', CIRCLE: 'Circular' }
const qrLabels: Record<string, string> = { CLASSIC: 'Negro clásico', ACCENT: 'Color de acento' }

const design = reactive<Design>({
  template: 'CLASSIC',
  backgroundColor: '#FFFFFF',
  accentColor: '#0A162A',
  textColor: '#101828',
  photoStyle: 'SQUARE',
  qrStyle: 'CLASSIC',
  decorations: [],
})
const defaults = ref<Design | null>(null)
const options = reactive({ templates: [] as string[], photoStyles: [] as string[], qrStyles: [] as string[], maxDecorations: 12 })

const profile = ref<Profile | null>(null)
const photoUrl = ref('')
const stickers = reactive({ global: [] as string[], mine: [] as string[], maxUserStickers: 20 })
const stickerUrls = reactive<Record<string, string>>({})

const selected = ref<number | null>(null)
const loading = ref(true)
const busy = ref(false)
const fb = reactive({ ok: '', err: '' })
const stickerInput = ref<HTMLInputElement | null>(null)
const card = ref<HTMLElement | null>(null)

const apiUrl = () => useRuntimeConfig().public.apiUrl as string

const selectedDecoration = computed(() =>
  selected.value === null ? null : design.decorations[selected.value] ?? null,
)

// Texto sobre el acento: blanco u oscuro según luminancia (igual que el backend).
function luminance(hex: string) {
  const c = [1, 3, 5].map((i) => {
    const v = parseInt(hex.substring(i, i + 2), 16) / 255
    return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4)
  })
  return 0.2126 * c[0]! + 0.7152 * c[1]! + 0.0722 * c[2]!
}
function contrast(a: string, b: string) {
  const [l1, l2] = [luminance(a), luminance(b)]
  return (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05)
}
const onAccent = computed(() => (contrast(design.accentColor, '#FFFFFF') >= 3 ? '#FFFFFF' : '#101828'))

// Avisos en vivo (el backend valida igual).
const warnings = computed(() => {
  const list: string[] = []
  if (contrast(design.textColor, design.backgroundColor) < 3) list.push('El texto no contrasta lo suficiente con el fondo.')
  if (design.qrStyle === 'ACCENT' && contrast(design.accentColor, '#FFFFFF') < 4.5) list.push('El acento es muy claro para el QR.')
  return list
})

function stickerKey(d: { source: string, file: string }) {
  return `${d.source}:${d.file}`
}

async function ensureStickerUrl(source: 'GLOBAL' | 'USER', file: string) {
  const key = stickerKey({ source, file })
  if (stickerUrls[key]) return

  if (source === 'GLOBAL') {
    stickerUrls[key] = `${apiUrl()}/Stickers/image/${encodeURIComponent(file)}`
  } else {
    stickerUrls[key] = (await fetchAuthedObjectUrl(`/CredentialDesign/stickers/mine/${encodeURIComponent(file)}`)) ?? ''
  }
}

function applyDesign(d: Design) {
  Object.assign(design, { ...d, decorations: d.decorations.map(x => ({ ...x })) })
  selected.value = null
  d.decorations.forEach(x => ensureStickerUrl(x.source, x.file))
}

async function loadStickers() {
  const data = await apiFetch<{ global: string[], mine: string[], maxUserStickers: number }>('/CredentialDesign/stickers')
  Object.assign(stickers, data)
  data.global.forEach(f => ensureStickerUrl('GLOBAL', f))
  data.mine.forEach(f => ensureStickerUrl('USER', f))
}

async function load() {
  loading.value = true
  fb.err = ''

  try {
    const [designData, me] = await Promise.all([
      apiFetch<{ design: Design, defaults: Design, options: typeof options }>('/CredentialDesign'),
      apiFetch<Profile>('/Profile/me'),
    ])

    profile.value = me
    defaults.value = designData.defaults
    Object.assign(options, designData.options)
    applyDesign(designData.design)

    photoUrl.value = (await fetchAuthedObjectUrl('/Profile/photo/modified')) ?? ''
    await loadStickers()
  } catch (err) {
    fb.err = err instanceof Error ? err.message : 'No fue posible cargar el diseño.'
  } finally {
    loading.value = false
  }
}

function addSticker(source: 'GLOBAL' | 'USER', file: string) {
  if (design.decorations.length >= options.maxDecorations) {
    fb.err = `Puedes colocar como máximo ${options.maxDecorations} stickers.`
    return
  }
  design.decorations.push({ source, file, x: 0.5, y: 0.5, size: 0.18, rotation: 0 })
  selected.value = design.decorations.length - 1
}

function removeSelected() {
  if (selected.value === null) return
  design.decorations.splice(selected.value, 1)
  selected.value = null
}

// --- Arrastrar stickers sobre la vista previa ---
let dragIndex: number | null = null

function startDrag(index: number, event: PointerEvent) {
  selected.value = index
  dragIndex = index
  ;(event.target as HTMLElement).setPointerCapture(event.pointerId)
}

function onDrag(event: PointerEvent) {
  if (dragIndex === null || !card.value) return
  const rect = card.value.getBoundingClientRect()
  const d = design.decorations[dragIndex]
  if (!d) return
  d.x = Math.min(1, Math.max(0, (event.clientX - rect.left) / rect.width))
  d.y = Math.min(1, Math.max(0, (event.clientY - rect.top) / rect.height))
}

function endDrag() {
  dragIndex = null
}

// --- Stickers propios ---
function pickSticker() {
  stickerInput.value?.click()
}

async function uploadSticker(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file) return

  fb.ok = ''
  fb.err = ''
  busy.value = true

  try {
    const form = new FormData()
    form.append('file', file)
    const result = await apiFetch<{ message: string, file: string }>('/CredentialDesign/stickers', { method: 'POST', body: form })
    await loadStickers()
    addSticker('USER', result.file)
    fb.ok = result.message
  } catch (err) {
    fb.err = err instanceof Error ? err.message : 'No fue posible subir el sticker.'
  } finally {
    busy.value = false
  }
}

async function deleteMySticker(file: string) {
  if (!confirm('¿Eliminar este sticker? También se quitará de tu diseño.')) return

  try {
    await apiFetch(`/CredentialDesign/stickers/mine/${encodeURIComponent(file)}`, { method: 'DELETE' })
    design.decorations = design.decorations.filter(d => !(d.source === 'USER' && d.file === file))
    selected.value = null
    await loadStickers()
  } catch (err) {
    fb.err = err instanceof Error ? err.message : 'No fue posible eliminar el sticker.'
  }
}

// --- Acciones ---
function payload() {
  return JSON.stringify({ ...design, decorations: design.decorations })
}

async function save(silent = false) {
  fb.ok = ''
  fb.err = ''
  busy.value = true

  try {
    const result = await apiFetch<{ message: string, design: Design }>('/CredentialDesign', { method: 'PUT', body: payload() })
    applyDesign(result.design)
    if (!silent) fb.ok = result.message
    return true
  } catch (err) {
    fb.err = err instanceof Error ? err.message : 'No fue posible guardar el diseño.'
    return false
  } finally {
    busy.value = false
  }
}

async function reset() {
  if (!confirm('¿Restaurar el diseño por defecto? Se perderán los cambios guardados.')) return

  busy.value = true
  fb.ok = ''
  fb.err = ''

  try {
    const result = await apiFetch<{ message: string, design: Design }>('/CredentialDesign', { method: 'DELETE' })
    applyDesign(result.design)
    fb.ok = result.message
  } catch (err) {
    fb.err = err instanceof Error ? err.message : 'No fue posible restaurar el diseño.'
  } finally {
    busy.value = false
  }
}

async function previewPdf() {
  fb.ok = ''
  fb.err = ''
  busy.value = true

  try {
    const response = await fetch(`${apiUrl()}/CredentialDesign/preview`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${getAuthToken()}` },
      body: payload(),
    })

    if (!response.ok) {
      const data = await response.json().catch(() => ({}))
      throw new Error(data.message ?? 'No fue posible generar la vista previa.')
    }

    window.open(URL.createObjectURL(await response.blob()), '_blank')
  } catch (err) {
    fb.err = err instanceof Error ? err.message : 'No fue posible generar la vista previa.'
  } finally {
    busy.value = false
  }
}

async function generateCredential() {
  const text = profile.value?.credentialIssuedAt
    ? 'Se guardará el diseño y se generará una credencial nueva. El QR de la anterior dejará de funcionar. ¿Continuar?'
    : 'Se guardará el diseño y se generará tu credencial. ¿Continuar?'

  if (!confirm(text)) return
  if (!(await save(true))) return

  busy.value = true

  try {
    const result = await apiFetch<{ message: string }>('/Profile/credential/regenerate', { method: 'POST' })
    fb.ok = result.message
    profile.value = await apiFetch<Profile>('/Profile/me')
  } catch (err) {
    fb.err = err instanceof Error ? err.message : 'No fue posible generar la credencial.'
  } finally {
    busy.value = false
  }
}

async function downloadCredential() {
  if (!(await openAuthedFile('/Profile/credential'))) {
    fb.err = 'No fue posible descargar la credencial.'
  }
}

function onKey(event: KeyboardEvent) {
  if ((event.key === 'Delete' || event.key === 'Backspace') && selected.value !== null &&
      !(event.target instanceof HTMLInputElement)) {
    removeSelected()
  }
}

onMounted(() => {
  load()
  window.addEventListener('keydown', onKey)
})

onBeforeUnmount(() => {
  window.removeEventListener('keydown', onKey)
})
</script>

<template>
  <main class="design-page">
    <header class="topbar">
      <div class="brand">
        <span class="brand-mark">L</span>
        <div>
          <h1>Léxica</h1>
          <p>Diseña tu credencial</p>
        </div>
      </div>
      <div class="topbar-actions">
        <NuxtLink to="/perfil" class="link-button">← Mi perfil</NuxtLink>
        <NuxtLink to="/dashboard" class="link-button">Análisis</NuxtLink>
      </div>
    </header>

    <div v-if="loading" class="state">Cargando...</div>

    <div v-else class="layout">
      <!-- Vista previa -->
      <section class="preview-panel">
        <h2>Vista previa</h2>
        <p class="hint">Arrastra los stickers para moverlos. Tus datos (nombre, correo, rol, foto y QR) no se pueden cambiar aquí.</p>

        <div class="card-frame">
        <div
          ref="card"
          class="card"
          :class="`tpl-${design.template.toLowerCase()}`"
          :style="{ '--bg': design.backgroundColor, '--accent': design.accentColor, '--text': design.textColor, '--on-accent': onAccent }"
          @pointermove="onDrag"
          @pointerup="endDrag"
          @pointerleave="endDrag"
          @click.self="selected = null"
        >
          <!-- Clásica -->
          <template v-if="design.template === 'CLASSIC'">
            <div class="c-header">
              <strong class="c-brand">LÉXICA</strong>
              <span class="c-slogan">Analiza. Descubre. Comprende.</span>
              <span class="c-title">CREDENCIAL DE INSCRIPCIÓN</span>
            </div>
            <div class="c-body">
              <div class="photo" :class="`ph-${design.photoStyle.toLowerCase()}`"><img v-if="photoUrl" :src="photoUrl" alt=""></div>
              <div class="info">
                <strong>{{ profile?.nickname }}</strong>
                <span class="email">{{ profile?.email }}</span>
                <span><b>Rol:</b> {{ profile?.roleName }}</span>
                <span><b>Emitida:</b> {{ new Date().toLocaleDateString('es-GT') }}</span>
              </div>
              <div class="qr" :class="{ accent: design.qrStyle === 'ACCENT' }"><svg class="qr-box" viewBox="0 0 29 29" aria-hidden="true"><rect width="29" height="29" fill="#fff" /><g :fill="design.qrStyle === 'ACCENT' ? design.accentColor : '#000'"><path d="M2 2h7v7H2zM20 2h7v7h-7zM2 20h7v7H2z" /><path fill="#fff" d="M3 3h5v5H3zM21 3h5v5h-5zM3 21h5v5H3z" /><path d="M4 4h3v3H4zM22 4h3v3h-3zM4 22h3v3H4zM11 2h2v2h-2zM14 4h2v3h-2zM11 8h3v2h-3zM16 9h2v2h-2zM2 11h3v2H2zM7 12h3v2H7zM12 12h5v2h-5zM19 11h2v3h-2zM23 12h4v2h-4zM11 15h2v4h-2zM15 16h3v2h-3zM20 16h2v2h-2zM24 17h3v2h-3zM14 20h2v3h-2zM18 20h4v2h-4zM11 23h2v4h-2zM16 24h3v3h-3zM21 23h2v2h-2zM24 22h3v2h-3zM21 26h6v1h-6z" /></g></svg><small>Escanéame para iniciar sesión</small></div>
            </div>
            <div class="footer">Esta credencial es personal e intransferible.</div>
          </template>

          <!-- Banda lateral -->
          <template v-else-if="design.template === 'LATERAL'">
            <div class="l-band">
              <strong class="c-brand">LÉXICA</strong>
              <span class="c-slogan">Analiza. Descubre. Comprende.</span>
              <div class="photo" :class="`ph-${design.photoStyle.toLowerCase()}`"><img v-if="photoUrl" :src="photoUrl" alt=""></div>
              <span class="c-title">CREDENCIAL</span>
            </div>
            <div class="l-body">
              <span class="l-title">CREDENCIAL DE INSCRIPCIÓN</span>
              <div class="l-row">
                <div class="info">
                  <strong>{{ profile?.nickname }}</strong>
                  <span class="email">{{ profile?.email }}</span>
                  <span><b>Rol:</b> {{ profile?.roleName }}</span>
                  <span><b>Emitida:</b> {{ new Date().toLocaleDateString('es-GT') }}</span>
                </div>
                <div class="qr" :class="{ accent: design.qrStyle === 'ACCENT' }"><svg class="qr-box" viewBox="0 0 29 29" aria-hidden="true"><rect width="29" height="29" fill="#fff" /><g :fill="design.qrStyle === 'ACCENT' ? design.accentColor : '#000'"><path d="M2 2h7v7H2zM20 2h7v7h-7zM2 20h7v7H2z" /><path fill="#fff" d="M3 3h5v5H3zM21 3h5v5h-5zM3 21h5v5H3z" /><path d="M4 4h3v3H4zM22 4h3v3h-3zM4 22h3v3H4zM11 2h2v2h-2zM14 4h2v3h-2zM11 8h3v2h-3zM16 9h2v2h-2zM2 11h3v2H2zM7 12h3v2H7zM12 12h5v2h-5zM19 11h2v3h-2zM23 12h4v2h-4zM11 15h2v4h-2zM15 16h3v2h-3zM20 16h2v2h-2zM24 17h3v2h-3zM14 20h2v3h-2zM18 20h4v2h-4zM11 23h2v4h-2zM16 24h3v3h-3zM21 23h2v2h-2zM24 22h3v2h-3zM21 26h6v1h-6z" /></g></svg><small>Escanéame para iniciar sesión</small></div>
              </div>
              <div class="footer">Esta credencial es personal e intransferible.</div>
            </div>
          </template>

          <!-- Minimalista -->
          <template v-else>
            <div class="m-top"><strong class="c-brand">LÉXICA</strong><span>CREDENCIAL DE INSCRIPCIÓN</span></div>
            <div class="m-line" />
            <div class="c-body">
              <div class="photo" :class="`ph-${design.photoStyle.toLowerCase()}`"><img v-if="photoUrl" :src="photoUrl" alt=""></div>
              <div class="info">
                <strong>{{ profile?.nickname }}</strong>
                <span class="email">{{ profile?.email }}</span>
                <span><b>Rol:</b> {{ profile?.roleName }}</span>
                <span><b>Emitida:</b> {{ new Date().toLocaleDateString('es-GT') }}</span>
              </div>
              <div class="qr" :class="{ accent: design.qrStyle === 'ACCENT' }"><svg class="qr-box" viewBox="0 0 29 29" aria-hidden="true"><rect width="29" height="29" fill="#fff" /><g :fill="design.qrStyle === 'ACCENT' ? design.accentColor : '#000'"><path d="M2 2h7v7H2zM20 2h7v7h-7zM2 20h7v7H2z" /><path fill="#fff" d="M3 3h5v5H3zM21 3h5v5h-5zM3 21h5v5H3z" /><path d="M4 4h3v3H4zM22 4h3v3h-3zM4 22h3v3H4zM11 2h2v2h-2zM14 4h2v3h-2zM11 8h3v2h-3zM16 9h2v2h-2zM2 11h3v2H2zM7 12h3v2H7zM12 12h5v2h-5zM19 11h2v3h-2zM23 12h4v2h-4zM11 15h2v4h-2zM15 16h3v2h-3zM20 16h2v2h-2zM24 17h3v2h-3zM14 20h2v3h-2zM18 20h4v2h-4zM11 23h2v4h-2zM16 24h3v3h-3zM21 23h2v2h-2zM24 22h3v2h-3zM21 26h6v1h-6z" /></g></svg><small>Escanéame para iniciar sesión</small></div>
            </div>
            <div class="footer">Esta credencial es personal e intransferible.</div>
          </template>

          <!-- Stickers -->
          <img
            v-for="(d, i) in design.decorations"
            :key="i"
            class="sticker"
            :class="{ active: selected === i }"
            :src="stickerUrls[stickerKey(d)]"
            alt=""
            draggable="false"
            :style="{ left: `${d.x * 100}%`, top: `${d.y * 100}%`, width: `${d.size * 100}%`, transform: `translate(-50%, -50%) rotate(${d.rotation}deg)` }"
            @pointerdown.prevent="startDrag(i, $event)"
          >
        </div>
        </div>

        <div v-if="selectedDecoration" class="sticker-controls">
          <label>Tamaño <input v-model.number="selectedDecoration.size" type="range" min="0.06" max="0.45" step="0.01"></label>
          <label>Rotación <input v-model.number="selectedDecoration.rotation" type="range" min="-180" max="180" step="1"></label>
          <button type="button" class="danger" @click="removeSelected">Quitar sticker</button>
        </div>

        <ul v-if="warnings.length" class="warnings">
          <li v-for="w in warnings" :key="w">{{ w }}</li>
        </ul>

        <div class="actions">
          <button type="button" class="primary" :disabled="busy" @click="save()">Guardar diseño</button>
          <button type="button" class="ghost" :disabled="busy" @click="reset">Restaurar diseño</button>
          <button type="button" class="ghost" :disabled="busy" @click="previewPdf">Vista previa PDF</button>
          <button type="button" class="primary" :disabled="busy" @click="generateCredential">
            {{ profile?.credentialIssuedAt ? 'Actualizar mi credencial' : 'Generar mi credencial' }}
          </button>
          <button v-if="profile?.credentialIssuedAt" type="button" class="ghost" :disabled="busy" @click="downloadCredential">Descargar credencial</button>
        </div>

        <div v-if="fb.err" class="feedback err" role="alert">{{ fb.err }}</div>
        <div v-if="fb.ok" class="feedback ok" role="status">{{ fb.ok }}</div>
      </section>

      <!-- Controles -->
      <section class="controls">
        <div class="group">
          <h3>Plantilla</h3>
          <div class="chips">
            <button v-for="t in options.templates" :key="t" type="button" :class="{ on: design.template === t }" @click="design.template = t">
              {{ templateLabels[t] ?? t }}
            </button>
          </div>
        </div>

        <div class="group">
          <h3>Colores</h3>
          <label class="color">Fondo <input v-model="design.backgroundColor" type="color"><code>{{ design.backgroundColor }}</code></label>
          <label class="color">Acento <input v-model="design.accentColor" type="color"><code>{{ design.accentColor }}</code></label>
          <label class="color">Texto <input v-model="design.textColor" type="color"><code>{{ design.textColor }}</code></label>
        </div>

        <div class="group">
          <h3>Foto</h3>
          <div class="chips">
            <button v-for="p in options.photoStyles" :key="p" type="button" :class="{ on: design.photoStyle === p }" @click="design.photoStyle = p">
              {{ photoLabels[p] ?? p }}
            </button>
          </div>
        </div>

        <div class="group">
          <h3>Código QR</h3>
          <div class="chips">
            <button v-for="q in options.qrStyles" :key="q" type="button" :class="{ on: design.qrStyle === q }" @click="design.qrStyle = q">
              {{ qrLabels[q] ?? q }}
            </button>
          </div>
        </div>

        <div class="group">
          <h3>Stickers del sistema</h3>
          <div v-if="stickers.global.length" class="sticker-grid">
            <button v-for="f in stickers.global" :key="f" type="button" class="sticker-thumb" @click="addSticker('GLOBAL', f)">
              <img :src="stickerUrls[`GLOBAL:${f}`]" alt="">
            </button>
          </div>
          <p v-else class="hint">No hay stickers del sistema todavía.</p>
        </div>

        <div class="group">
          <h3>Mis stickers ({{ stickers.mine.length }}/{{ stickers.maxUserStickers }})</h3>
          <div class="sticker-grid">
            <div v-for="f in stickers.mine" :key="f" class="sticker-thumb own">
              <button type="button" @click="addSticker('USER', f)"><img :src="stickerUrls[`USER:${f}`]" alt=""></button>
              <button type="button" class="remove" title="Eliminar sticker" @click="deleteMySticker(f)">✕</button>
            </div>
          </div>
          <button type="button" class="ghost full" :disabled="busy || stickers.mine.length >= stickers.maxUserStickers" @click="pickSticker">
            Subir sticker (PNG, JPG o WEBP, máx. 1 MB)
          </button>
          <input ref="stickerInput" type="file" accept="image/png,image/jpeg,image/webp" hidden @change="uploadSticker">
        </div>
      </section>
    </div>
  </main>
</template>

<style scoped>
.design-page {
  min-height: 100vh;
  background:
    linear-gradient(180deg, rgba(6, 12, 28, 0.55) 0%, rgba(6, 12, 28, 0.78) 100%),
    url('/images/apartado2.png') center / cover no-repeat fixed,
    #07101f;
  color: #eaf1fb;
  font-family: 'Poppins', 'Segoe UI', system-ui, sans-serif;
  padding: 24px;
}
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
.link-button {
  padding: 9px 14px; border-radius: 9px; border: 1px solid rgba(73, 151, 208, 0.25);
  background: rgba(73, 151, 208, 0.06); color: #a9c5e3; text-decoration: none; font-size: 13px; font-weight: 600;
}
.state { text-align: center; padding: 40px; color: #86a5c8; }

.layout { display: grid; grid-template-columns: minmax(0, 1.5fr) minmax(260px, 1fr); gap: 20px; align-items: start; }
.preview-panel, .controls {
  padding: 20px; border-radius: 14px; background: rgba(10, 21, 40, 0.75); border: 1px solid rgba(73, 151, 208, 0.18);
}
h2 { margin: 0 0 4px; font-size: 15px; }
h3 { margin: 0 0 8px; font-size: 12.5px; color: #cddcee; }
.hint { margin: 0 0 12px; font-size: 11.5px; color: #6682a7; }

/* Credencial: 241 x 156 pt. El marco es el contenedor: 1pt = 100cqw / 241 */
.card-frame { container-type: inline-size; width: 100%; max-width: 620px; }
.card {
  --pt: calc(100cqw / 241);
  position: relative;
  width: 100%;
  aspect-ratio: 241 / 156;
  background: var(--bg);
  color: var(--text);
  overflow: hidden;
  border-radius: 10px;
  box-shadow: 0 20px 50px rgba(0, 0, 0, 0.35);
  user-select: none;
  touch-action: none;
  font-family: 'Lato', 'Segoe UI', sans-serif;
}
.card * { box-sizing: border-box; }
.card .c-brand { font-size: calc(11 * var(--pt)); }
.card .c-slogan { font-size: calc(4 * var(--pt)); }
.card .c-title { font-size: calc(5.5 * var(--pt)); font-weight: 700; }

.c-header {
  height: calc(34 * var(--pt)); background: var(--accent); color: var(--on-accent);
  padding: calc(2 * var(--pt)) calc(12 * var(--pt)); display: flex; flex-direction: column; justify-content: center;
}
.c-body {
  height: calc(111 * var(--pt)); display: flex; align-items: center; gap: calc(8 * var(--pt));
  padding: calc(5 * var(--pt)) calc(10 * var(--pt)) 0;
}
.tpl-minimal .c-body { height: calc(106 * var(--pt)); padding: calc(6 * var(--pt)) 0 0; }
.photo { width: calc(68 * var(--pt)); height: calc(68 * var(--pt)); flex: none; overflow: hidden; background: rgba(0, 0, 0, 0.08); align-self: flex-start; }
.photo img { width: 100%; height: 100%; object-fit: cover; display: block; }
.ph-square { border: calc(1.2 * var(--pt)) solid var(--accent); }
.ph-rounded { border-radius: 18%; }
.ph-circle { border-radius: 50%; }
.info { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: calc(2 * var(--pt)); font-size: calc(5.2 * var(--pt)); }
.info strong { font-size: calc(9 * var(--pt)); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.info .email { font-size: calc(4.8 * var(--pt)); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; margin-bottom: calc(2 * var(--pt)); }
.qr { width: calc(58 * var(--pt)); flex: none; text-align: center; }
.qr-box { width: 100%; aspect-ratio: 1; display: block; }
.qr small { display: block; font-size: calc(3.5 * var(--pt)); margin-top: calc(1 * var(--pt)); }
.footer { font-size: calc(3.2 * var(--pt)); padding: 0 calc(10 * var(--pt)); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }

.tpl-lateral { display: flex; }
.l-band {
  width: calc(82 * var(--pt)); height: 100%; background: var(--accent); color: var(--on-accent);
  padding: calc(8 * var(--pt)) calc(7 * var(--pt)); display: flex; flex-direction: column; align-items: center; text-align: center;
}
.l-band .c-brand { font-size: calc(10 * var(--pt)); }
.l-band .c-slogan { font-size: calc(3.4 * var(--pt)); }
.l-band .photo { width: calc(64 * var(--pt)); height: calc(64 * var(--pt)); margin: calc(8 * var(--pt)) 0 calc(6 * var(--pt)); align-self: center; }
.l-band .c-title { font-size: calc(5 * var(--pt)); }
.l-body { flex: 1; min-width: 0; padding: calc(10 * var(--pt)) calc(9 * var(--pt)) 0; display: flex; flex-direction: column; }
.l-title { font-size: calc(5.5 * var(--pt)); font-weight: 700; }
.l-row { height: calc(108 * var(--pt)); display: flex; align-items: center; gap: calc(4 * var(--pt)); }
.l-row .qr { width: calc(54 * var(--pt)); }
.l-body .footer { padding: 0; }

.tpl-minimal { padding: calc(9 * var(--pt)) calc(12 * var(--pt)) 0; }
.m-top { display: flex; justify-content: space-between; align-items: flex-end; }
.m-top span { font-size: calc(5 * var(--pt)); font-weight: 700; }
.m-line { height: calc(2 * var(--pt)); background: var(--accent); margin-top: calc(3 * var(--pt)); }
.tpl-minimal .photo { width: calc(66 * var(--pt)); height: calc(66 * var(--pt)); }
.tpl-minimal .qr { width: calc(56 * var(--pt)); }
.tpl-minimal .footer { padding: 0; }

.sticker {
  position: absolute; aspect-ratio: 1; object-fit: contain; cursor: grab; touch-action: none;
}
.sticker.active { outline: 2px dashed #4997d0; outline-offset: 2px; }

.sticker-controls { display: flex; flex-wrap: wrap; gap: 12px; align-items: center; margin-top: 12px; font-size: 12px; color: #86a5c8; }
.sticker-controls label { display: flex; align-items: center; gap: 6px; }

.warnings { margin: 12px 0 0; padding: 10px 14px 10px 28px; border-radius: 10px; background: rgba(251, 146, 60, 0.12); color: #fdba74; font-size: 12px; }

.actions { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 16px; }
button { border: 0; border-radius: 9px; padding: 9px 13px; cursor: pointer; font: inherit; font-size: 12.5px; font-weight: 600; }
button:disabled { opacity: 0.45; cursor: not-allowed; }
.primary { background: linear-gradient(135deg, #2f6fb8, #4997d0); color: white; }
.ghost { background: rgba(73, 151, 208, 0.1); color: #cddcee; }
.danger { background: rgba(248, 113, 113, 0.15); color: #fca5a5; }
.full { width: 100%; margin-top: 8px; }

.feedback { margin-top: 12px; padding: 10px 12px; border-radius: 10px; font-size: 12px; }
.feedback.err { background: rgba(127, 29, 29, 0.15); color: #fca5a5; border: 1px solid rgba(248, 113, 113, 0.3); }
.feedback.ok { background: rgba(20, 83, 45, 0.25); color: #86efac; border: 1px solid rgba(74, 222, 128, 0.3); }

.group { margin-bottom: 18px; }
.chips { display: flex; flex-wrap: wrap; gap: 6px; }
.chips button { background: rgba(73, 151, 208, 0.08); color: #a9c5e3; border: 1px solid rgba(73, 151, 208, 0.2); }
.chips button.on { background: rgba(73, 151, 208, 0.25); color: #fff; border-color: #4997d0; }
.color { display: flex; align-items: center; gap: 10px; font-size: 12px; color: #86a5c8; margin-bottom: 6px; }
.color input { width: 38px; height: 28px; border: 0; background: none; padding: 0; cursor: pointer; }
.color code { font-size: 11px; color: #cddcee; }

.sticker-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(56px, 1fr)); gap: 8px; }
.sticker-thumb {
  position: relative; aspect-ratio: 1; padding: 4px; border-radius: 10px;
  background: rgba(255, 255, 255, 0.06); border: 1px solid rgba(73, 151, 208, 0.18);
}
.sticker-thumb img { width: 100%; height: 100%; object-fit: contain; display: block; }
.sticker-thumb.own > button:first-child { width: 100%; height: 100%; padding: 0; background: none; }
.sticker-thumb .remove {
  position: absolute; top: -6px; right: -6px; width: 20px; height: 20px; padding: 0; border-radius: 50%;
  background: #7f1d1d; color: #fff; font-size: 10px;
}

@media (max-width: 900px) {
  .layout { grid-template-columns: 1fr; }
  .design-page { padding: 16px; }
}
</style>
