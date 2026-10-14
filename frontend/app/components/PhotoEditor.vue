<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { apiFetch } from '~/services/api'

// Editor de la foto de perfil: cámara o archivo -> recorte (mover/zoom) ->
// filtros -> stickers -> guardar. Envía la foto ORIGINAL (sin editar, para
// validar el rostro) y la MODIFICADA (la que se ve en el sitio y la credencial).
const emit = defineEmits<{ (e: 'saved', message: string): void, (e: 'close'): void }>()

const OUTPUT_SIZE = 800
const MAX_SOURCE_BYTES = 5 * 1024 * 1024

interface PlacedSticker {
  img: HTMLImageElement
  x: number
  y: number
  size: number
  rotation: number
}

const step = ref<'source' | 'camera' | 'edit'>('source')
const video = ref<HTMLVideoElement | null>(null)
const canvas = ref<HTMLCanvasElement | null>(null)
const fileInput = ref<HTMLInputElement | null>(null)
let stream: MediaStream | null = null

const error = ref('')
const saving = ref(false)

let sourceBlob: Blob | null = null
let photo: HTMLImageElement | null = null

const view = reactive({ zoom: 1, offsetX: 0, offsetY: 0 })
const filter = ref(photoFilters[0]!.css)
const stickers = ref<PlacedSticker[]>([])
const selected = ref<number | null>(null)
const catalog = reactive({ global: [] as string[], mine: [] as { file: string, url: string }[] })

const apiUrl = () => useRuntimeConfig().public.apiUrl as string

// ---------------- Fuente ----------------

async function startCamera() {
  error.value = ''

  try {
    if (!navigator.mediaDevices?.getUserMedia) {
      throw new Error('El navegador no permite acceder a la cámara.')
    }

    stream = await navigator.mediaDevices.getUserMedia({
      video: { facingMode: 'user', width: { ideal: 1280 }, height: { ideal: 960 } },
      audio: false,
    })

    step.value = 'camera'
    await nextTick()

    if (video.value) {
      video.value.srcObject = stream
      await video.value.play()
    }
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'No fue posible acceder a la cámara.'
  }
}

function stopCamera() {
  stream?.getTracks().forEach(t => t.stop())
  stream = null
  if (video.value) video.value.srcObject = null
}

async function capture() {
  const v = video.value
  if (!v || !v.videoWidth) {
    error.value = 'La cámara todavía no está lista.'
    return
  }

  const c = document.createElement('canvas')
  c.width = v.videoWidth
  c.height = v.videoHeight
  c.getContext('2d')!.drawImage(v, 0, 0)

  const blob = await new Promise<Blob | null>(r => c.toBlob(r, 'image/jpeg', 0.92))
  stopCamera()

  if (blob) await useSource(blob)
}

function pickFile() {
  fileInput.value?.click()
}

async function onFile(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file) return

  if (!/^image\/(jpeg|png|webp)$/.test(file.type)) {
    error.value = 'Usa una imagen JPG, PNG o WEBP.'
    return
  }

  await useSource(file)
}

async function useSource(blob: Blob) {
  error.value = ''

  const img = await loadImage(URL.createObjectURL(blob))

  // La original se envía tal cual si cabe; si pesa mucho se reduce.
  sourceBlob = blob.size <= MAX_SOURCE_BYTES ? blob : await downscale(img)
  photo = img

  Object.assign(view, { zoom: 1, offsetX: 0, offsetY: 0 })
  filter.value = photoFilters[0]!.css
  stickers.value = []
  selected.value = null

  step.value = 'edit'
  await nextTick()
  render()
}

async function downscale(img: HTMLImageElement): Promise<Blob> {
  const scale = Math.min(1, 2000 / Math.max(img.width, img.height))
  const c = document.createElement('canvas')
  c.width = Math.round(img.width * scale)
  c.height = Math.round(img.height * scale)
  c.getContext('2d')!.drawImage(img, 0, 0, c.width, c.height)
  return (await new Promise<Blob | null>(r => c.toBlob(r, 'image/jpeg', 0.9)))!
}

function loadImage(src: string, crossOrigin = false): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const img = new Image()
    if (crossOrigin) img.crossOrigin = 'anonymous'
    img.onload = () => resolve(img)
    img.onerror = reject
    img.src = src
  })
}

// ---------------- Dibujo ----------------

function baseScale() {
  return photo ? Math.max(OUTPUT_SIZE / photo.width, OUTPUT_SIZE / photo.height) : 1
}

function clampOffset() {
  if (!photo) return
  const s = baseScale() * view.zoom
  const maxX = Math.max(0, (photo.width * s - OUTPUT_SIZE) / 2)
  const maxY = Math.max(0, (photo.height * s - OUTPUT_SIZE) / 2)
  view.offsetX = Math.min(maxX, Math.max(-maxX, view.offsetX))
  view.offsetY = Math.min(maxY, Math.max(-maxY, view.offsetY))
}

function draw(ctx: CanvasRenderingContext2D, showSelection: boolean) {
  if (!photo) return

  const s = baseScale() * view.zoom
  const w = photo.width * s
  const h = photo.height * s

  ctx.clearRect(0, 0, OUTPUT_SIZE, OUTPUT_SIZE)
  ctx.filter = filter.value
  ctx.drawImage(photo, (OUTPUT_SIZE - w) / 2 + view.offsetX, (OUTPUT_SIZE - h) / 2 + view.offsetY, w, h)
  ctx.filter = 'none'

  stickers.value.forEach((st, i) => {
    const size = st.size * OUTPUT_SIZE
    const ratio = st.img.height / st.img.width
    const sw = ratio > 1 ? size / ratio : size
    const sh = ratio > 1 ? size : size * ratio

    ctx.save()
    ctx.translate(st.x * OUTPUT_SIZE, st.y * OUTPUT_SIZE)
    ctx.rotate((st.rotation * Math.PI) / 180)
    ctx.drawImage(st.img, -sw / 2, -sh / 2, sw, sh)

    if (showSelection && selected.value === i) {
      ctx.strokeStyle = 'rgba(255,255,255,0.95)'
      ctx.lineWidth = 3
      ctx.setLineDash([10, 6])
      ctx.strokeRect(-size / 2, -size / 2, size, size)
    }

    ctx.restore()
  })
}

function render() {
  const c = canvas.value
  if (!c) return
  c.width = OUTPUT_SIZE
  c.height = OUTPUT_SIZE
  draw(c.getContext('2d')!, true)
}

watch([filter, () => view.zoom], () => {
  clampOffset()
  render()
})

// ---------------- Arrastre (foto y stickers) ----------------

let drag: { kind: 'photo' | 'sticker', startX: number, startY: number, baseX: number, baseY: number } | null = null

function toCanvas(event: PointerEvent) {
  const rect = canvas.value!.getBoundingClientRect()
  return {
    x: ((event.clientX - rect.left) / rect.width) * OUTPUT_SIZE,
    y: ((event.clientY - rect.top) / rect.height) * OUTPUT_SIZE,
  }
}

function onPointerDown(event: PointerEvent) {
  const p = toCanvas(event)

  for (let i = stickers.value.length - 1; i >= 0; i--) {
    const st = stickers.value[i]!
    const half = (st.size * OUTPUT_SIZE) / 2
    if (Math.abs(p.x - st.x * OUTPUT_SIZE) < half && Math.abs(p.y - st.y * OUTPUT_SIZE) < half) {
      selected.value = i
      drag = { kind: 'sticker', startX: p.x, startY: p.y, baseX: st.x, baseY: st.y }
      canvas.value!.setPointerCapture(event.pointerId)
      render()
      return
    }
  }

  selected.value = null
  drag = { kind: 'photo', startX: p.x, startY: p.y, baseX: view.offsetX, baseY: view.offsetY }
  canvas.value!.setPointerCapture(event.pointerId)
  render()
}

function onPointerMove(event: PointerEvent) {
  if (!drag) return
  const p = toCanvas(event)
  const dx = p.x - drag.startX
  const dy = p.y - drag.startY

  if (drag.kind === 'photo') {
    view.offsetX = drag.baseX + dx
    view.offsetY = drag.baseY + dy
    clampOffset()
  } else if (selected.value !== null) {
    const st = stickers.value[selected.value]!
    st.x = Math.min(1, Math.max(0, drag.baseX + dx / OUTPUT_SIZE))
    st.y = Math.min(1, Math.max(0, drag.baseY + dy / OUTPUT_SIZE))
  }

  render()
}

function onPointerUp() {
  drag = null
}

function onWheel(event: WheelEvent) {
  view.zoom = Math.min(3, Math.max(1, view.zoom - event.deltaY * 0.0015))
}

// ---------------- Stickers ----------------

async function loadCatalog() {
  try {
    const data = await apiFetch<{ global: string[], mine: string[] }>('/CredentialDesign/stickers')
    catalog.global = data.global
    catalog.mine = []
    for (const file of data.mine) {
      const url = await fetchAuthedObjectUrl(`/CredentialDesign/stickers/mine/${encodeURIComponent(file)}`)
      if (url) catalog.mine.push({ file, url })
    }
  } catch {
    // Sin stickers: el editor funciona igual.
  }
}

function globalUrl(file: string) {
  return `${apiUrl()}/Stickers/image/${encodeURIComponent(file)}`
}

async function addSticker(url: string, crossOrigin: boolean) {
  try {
    // crossOrigin: los del sistema vienen de la API; sin esto el canvas quedaría
    // "contaminado" y no se podría exportar la foto.
    const img = await loadImage(url, crossOrigin)
    stickers.value.push({ img, x: 0.5, y: 0.5, size: 0.3, rotation: 0 })
    selected.value = stickers.value.length - 1
    render()
  } catch {
    error.value = 'No fue posible cargar ese sticker.'
  }
}

function removeSelected() {
  if (selected.value === null) return
  stickers.value.splice(selected.value, 1)
  selected.value = null
  render()
}

// ---------------- Guardar ----------------

async function save() {
  if (!sourceBlob || !photo) return

  error.value = ''
  saving.value = true

  try {
    const out = document.createElement('canvas')
    out.width = OUTPUT_SIZE
    out.height = OUTPUT_SIZE
    draw(out.getContext('2d')!, false)

    const modified = await new Promise<Blob | null>(r => out.toBlob(r, 'image/jpeg', 0.9))
    if (!modified) throw new Error('No fue posible generar la foto editada.')

    const form = new FormData()
    form.append('original', sourceBlob, 'original.jpg')
    form.append('modified', modified, 'modified.jpg')

    const result = await apiFetch<{ message: string }>('/Profile/photo', { method: 'POST', body: form })
    emit('saved', result.message)
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'No fue posible guardar la foto.'
  } finally {
    saving.value = false
  }
}

function restart() {
  stopCamera()
  step.value = 'source'
  photo = null
  sourceBlob = null
}

function close() {
  stopCamera()
  emit('close')
}

onMounted(loadCatalog)
onBeforeUnmount(stopCamera)
</script>

<template>
  <div class="editor-backdrop" @click.self="close">
    <section class="editor" role="dialog" aria-label="Editor de foto de perfil">
      <header class="editor-head">
        <h2>Cambiar foto de perfil</h2>
        <button type="button" class="close" aria-label="Cerrar" @click="close">✕</button>
      </header>

      <!-- 1. Fuente -->
      <div v-if="step === 'source'" class="source">
        <p class="hint">Toma una foto con la cámara o sube una imagen. Debe verse un solo rostro.</p>
        <div class="source-actions">
          <button type="button" class="big" @click="startCamera">
            <AppIcon name="camara" :size="48" />
            Tomar foto
          </button>
          <button type="button" class="big" @click="pickFile">
            <AppIcon name="imagen" :size="48" />
            Subir imagen
          </button>
        </div>
        <input ref="fileInput" type="file" accept="image/jpeg,image/png,image/webp" hidden @change="onFile">
      </div>

      <!-- 2. Cámara -->
      <div v-else-if="step === 'camera'" class="camera">
        <video ref="video" autoplay playsinline muted />
        <div class="row">
          <button type="button" class="ghost" @click="restart">Cancelar</button>
          <button type="button" class="primary" @click="capture"><AppIcon name="camara" /> Capturar</button>
        </div>
      </div>

      <!-- 3. Edición -->
      <div v-else class="edit">
        <div class="stage">
          <canvas
            ref="canvas"
            @pointerdown.prevent="onPointerDown"
            @pointermove="onPointerMove"
            @pointerup="onPointerUp"
            @pointercancel="onPointerUp"
            @wheel.prevent="onWheel"
          />
          <p class="hint">Arrastra la foto para encuadrarla y los stickers para moverlos.</p>
          <label class="slider">Zoom <input v-model.number="view.zoom" type="range" min="1" max="3" step="0.01"></label>
          <template v-if="selected !== null && stickers[selected]">
            <label class="slider">Tamaño del sticker <input v-model.number="stickers[selected]!.size" type="range" min="0.08" max="0.8" step="0.01" @input="render"></label>
            <label class="slider">Rotación <input v-model.number="stickers[selected]!.rotation" type="range" min="-180" max="180" step="1" @input="render"></label>
            <button type="button" class="danger" @click="removeSelected">Quitar sticker</button>
          </template>
        </div>

        <div class="tools">
          <h3>Filtros</h3>
          <div class="chips">
            <button v-for="f in photoFilters" :key="f.id" type="button" :class="{ on: filter === f.css }" @click="filter = f.css">
              {{ f.label }}
            </button>
          </div>

          <h3>Stickers</h3>
          <div class="stickers">
            <button v-for="f in catalog.global" :key="`g-${f}`" type="button" @click="addSticker(globalUrl(f), true)">
              <img :src="globalUrl(f)" alt="" crossorigin="anonymous">
            </button>
            <button v-for="m in catalog.mine" :key="`m-${m.file}`" type="button" @click="addSticker(m.url, false)">
              <img :src="m.url" alt="">
            </button>
          </div>
          <p v-if="!catalog.global.length && !catalog.mine.length" class="hint">No hay stickers disponibles.</p>
        </div>
      </div>

      <div v-if="error" class="feedback err" role="alert">{{ error }}</div>

      <footer v-if="step === 'edit'" class="editor-foot">
        <button type="button" class="ghost" :disabled="saving" @click="restart">Elegir otra foto</button>
        <button type="button" class="primary" :disabled="saving" @click="save">
          {{ saving ? 'Guardando...' : 'Confirmar y guardar' }}
        </button>
      </footer>
    </section>
  </div>
</template>

<style scoped>
.editor-backdrop {
  position: fixed; inset: 0; z-index: 60; display: grid; place-items: center; padding: 16px;
  background: rgba(2, 6, 15, 0.75); backdrop-filter: blur(3px);
}
.editor {
  width: min(920px, 100%); max-height: calc(100vh - 32px); overflow-y: auto; padding: 22px; border-radius: 18px;
  background: #0a1528; border: 1px solid rgba(73, 151, 208, 0.25); color: #eaf1fb;
  box-shadow: 0 25px 70px rgba(0, 0, 0, 0.5); font-family: 'Poppins', 'Segoe UI', system-ui, sans-serif;
}
.editor-head { display: flex; justify-content: space-between; align-items: center; margin-bottom: 14px; }
.editor-head h2 { margin: 0; font-size: 18px; }
h3 { margin: 14px 0 8px; font-size: 12.5px; color: #cddcee; }
.hint { margin: 8px 0; font-size: 12px; color: #6682a7; }

button { border: 0; border-radius: 10px; padding: 10px 14px; cursor: pointer; font: inherit; font-size: 13px; font-weight: 600; }
button:disabled { opacity: 0.45; cursor: not-allowed; }
.close { width: 34px; height: 34px; padding: 0; background: rgba(73, 151, 208, 0.1); color: #cddcee; }
.primary { background: linear-gradient(135deg, #2f6fb8, #4997d0); color: #fff; display: inline-flex; align-items: center; gap: 8px; }
.ghost { background: rgba(73, 151, 208, 0.1); color: #cddcee; }
.danger { background: rgba(248, 113, 113, 0.15); color: #fca5a5; margin-top: 8px; }

.source-actions { display: grid; grid-template-columns: 1fr 1fr; gap: 14px; margin-top: 10px; }
.big {
  display: flex; flex-direction: column; align-items: center; gap: 10px; padding: 28px 12px;
  background: rgba(5, 11, 22, 0.6); border: 1px dashed rgba(73, 151, 208, 0.35); color: #cddcee;
}

.camera video { width: 100%; max-height: 60vh; border-radius: 14px; background: #050b16; display: block; }
.row { display: flex; justify-content: flex-end; gap: 10px; margin-top: 12px; }

.edit { display: grid; grid-template-columns: minmax(0, 1fr) minmax(220px, 300px); gap: 18px; }
.stage canvas {
  width: 100%; aspect-ratio: 1; border-radius: 14px; background: #050b16; touch-action: none; cursor: grab; display: block;
}
.slider { display: flex; align-items: center; gap: 10px; margin-top: 8px; font-size: 12px; color: #86a5c8; }
.slider input { flex: 1; }
.chips { display: flex; flex-wrap: wrap; gap: 6px; }
.chips button { padding: 6px 10px; font-size: 11.5px; background: rgba(73, 151, 208, 0.08); color: #a9c5e3; border: 1px solid rgba(73, 151, 208, 0.2); }
.chips button.on { background: rgba(73, 151, 208, 0.28); color: #fff; border-color: #4997d0; }
.stickers { display: grid; grid-template-columns: repeat(auto-fill, minmax(52px, 1fr)); gap: 8px; max-height: 260px; overflow-y: auto; }
.stickers button { aspect-ratio: 1; padding: 4px; background: rgba(255, 255, 255, 0.06); border: 1px solid rgba(73, 151, 208, 0.18); }
.stickers img { width: 100%; height: 100%; object-fit: contain; display: block; }

.feedback { margin-top: 12px; padding: 10px 12px; border-radius: 10px; font-size: 12px; }
.feedback.err { background: rgba(127, 29, 29, 0.15); color: #fca5a5; border: 1px solid rgba(248, 113, 113, 0.3); }
.editor-foot { display: flex; justify-content: flex-end; gap: 10px; margin-top: 16px; }

@media (max-width: 720px) {
  .edit { grid-template-columns: 1fr; }
  .source-actions { grid-template-columns: 1fr; }
}
</style>
