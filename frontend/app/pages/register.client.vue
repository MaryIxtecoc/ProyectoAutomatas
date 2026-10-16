<script setup lang="ts">
import { PHONE_PATTERN, PHONE_ERROR, sanitizePhoneInput, isValidPhone } from '~/utils/phone'
import {
  ref,
  watch,
  nextTick,
  onMounted,
  onBeforeUnmount
} from 'vue'

import { useRecaptcha } from 'vue3-recaptcha-v2'
import { apiFetch } from '~/services/api'

const step = ref<'form' | 'photo'>('form')

const email = ref('')
const phone = ref('')
const birthDate = ref('')
const nickname = ref('')
const password = ref('')

const notificationMethod = ref<
  'EMAIL' | 'WHATSAPP' | 'BOTH'
>('EMAIL')

const loading = ref(false)
const error = ref('')
const success = ref('')

// =====================================================
// reCAPTCHA
// =====================================================

const recaptchaEl = ref<HTMLElement | null>(null)

const recaptchaToken = ref('')
let renderRecaptcha = () => {}
let resetRecaptcha = () => {}

onMounted(() => {
  const recaptcha = useRecaptcha()
  if (recaptcha) {
    recaptchaToken.value = recaptcha.token
    renderRecaptcha = recaptcha.render
    resetRecaptcha = recaptcha.reset
  }
})

// =====================================================
// Cámara
// =====================================================

const video = ref<HTMLVideoElement | null>(null)
const captureCanvas = ref<HTMLCanvasElement | null>(null)
const editCanvas = ref<HTMLCanvasElement | null>(null)

const stream = ref<MediaStream | null>(null)
const cameraActive = ref(false)
const capturedDataUrl = ref('')

let baseImage: HTMLImageElement | null = null

// =====================================================
// Filtros
// =====================================================

const filters = photoFilters

const selectedFilter = ref(filters[0].css)

// =====================================================
// Stickers
// =====================================================

interface StickerOption {
  fileName: string
  url: string
}

interface PlacedSticker {
  key: number
  src: string
  x: number
  y: number
  scale: number
}

const availableStickers = ref<StickerOption[]>([])
const placedStickers = ref<PlacedSticker[]>([])
const activeStickerKey = ref<number | null>(null)

const stickerImageCache = new Map<
  string,
  HTMLImageElement
>()

const localStickerInput =
  ref<HTMLInputElement | null>(null)

let nextStickerKey = 1

let dragging = false

let dragOffset = {
  x: 0,
  y: 0
}

// =====================================================
// Stickers
// =====================================================

async function loadStickers() {
  try {
    const list = await apiFetch<StickerOption[]>(
      '/Stickers'
    )

    const apiUrl =
      useRuntimeConfig().public.apiUrl as string

    availableStickers.value = list.map(s => ({
      fileName: s.fileName,
      url: `${apiUrl}/Stickers/image/${s.fileName}`
    }))
  } catch {
    availableStickers.value = []
  }
}

function loadImage(
  src: string
): Promise<HTMLImageElement> {
  const cached = stickerImageCache.get(src)

  if (cached) {
    return Promise.resolve(cached)
  }

  return new Promise((resolve, reject) => {
    const img = new Image()

    img.onload = () => {
      stickerImageCache.set(src, img)
      resolve(img)
    }

    img.onerror = reject

    img.src = src
  })
}

async function addSticker(src: string) {
  try {
    await loadImage(src)
  } catch {
    error.value =
      'No fue posible cargar ese sticker.'
    return
  }

  const key = nextStickerKey++

  placedStickers.value.push({
    key,
    src,
    x: 0.5,
    y: 0.55,
    scale: 0.28
  })

  activeStickerKey.value = key

  await renderPreview()
}

function triggerLocalSticker() {
  localStickerInput.value?.click()
}

function onLocalStickerSelected(e: Event) {
  const input = e.target as HTMLInputElement

  const file = input.files?.[0]

  if (!file) return

  const reader = new FileReader()

  reader.onload = () => {
    if (typeof reader.result === 'string') {
      addSticker(reader.result)
    }
  }

  reader.readAsDataURL(file)

  input.value = ''
}

function removeActiveSticker() {
  if (activeStickerKey.value === null) {
    return
  }

  placedStickers.value =
    placedStickers.value.filter(
      s =>
        s.key !== activeStickerKey.value
    )

  activeStickerKey.value = null

  renderPreview()
}

function clearStickers() {
  placedStickers.value = []

  activeStickerKey.value = null

  renderPreview()
}

function activeScale(): number {
  return (
    placedStickers.value.find(
      s =>
        s.key === activeStickerKey.value
    )?.scale ?? 0.28
  )
}

function setActiveScale(
  value: number
) {
  const sticker =
    placedStickers.value.find(
      s =>
        s.key === activeStickerKey.value
    )

  if (!sticker) return

  sticker.scale = value

  renderPreview()
}

// =====================================================
// Drag de stickers
// =====================================================

function canvasPoint(
  evt: PointerEvent
) {
  const canvas = editCanvas.value!

  const rect =
    canvas.getBoundingClientRect()

  return {
    x:
      (evt.clientX - rect.left) *
      (canvas.width / rect.width),

    y:
      (evt.clientY - rect.top) *
      (canvas.height / rect.height)
  }
}

function onCanvasPointerDown(
  evt: PointerEvent
) {
  const canvas = editCanvas.value

  if (
    !canvas ||
    placedStickers.value.length === 0
  ) {
    return
  }

  const p = canvasPoint(evt)

  for (
    let i = placedStickers.value.length - 1;
    i >= 0;
    i--
  ) {
    const s = placedStickers.value[i]

    const size =
      canvas.width * s.scale

    const cx =
      s.x * canvas.width

    const cy =
      s.y * canvas.height

    if (
      Math.abs(p.x - cx) <
        size / 2 &&
      Math.abs(p.y - cy) <
        size / 2
    ) {
      activeStickerKey.value =
        s.key

      dragging = true

      dragOffset = {
        x: p.x - cx,
        y: p.y - cy
      }

      canvas.setPointerCapture(
        evt.pointerId
      )

      renderPreview()

      return
    }
  }
}

function onCanvasPointerMove(
  evt: PointerEvent
) {
  if (
    !dragging ||
    activeStickerKey.value === null
  ) {
    return
  }

  const canvas = editCanvas.value

  const sticker =
    placedStickers.value.find(
      s =>
        s.key ===
        activeStickerKey.value
    )

  if (!canvas || !sticker) {
    return
  }

  const p = canvasPoint(evt)

  sticker.x = Math.min(
    1,
    Math.max(
      0,
      (p.x - dragOffset.x) /
        canvas.width
    )
  )

  sticker.y = Math.min(
    1,
    Math.max(
      0,
      (p.y - dragOffset.y) /
        canvas.height
    )
  )

  renderPreview()
}

function onCanvasPointerUp() {
  dragging = false
}

// =====================================================
// Validación
// =====================================================

function validateForm(): boolean {
  if (
    !email.value ||
    !phone.value ||
    !birthDate.value ||
    !nickname.value ||
    !password.value
  ) {
    error.value =
      'Completa todos los campos.'

    return false
  }

  if (!isValidPhone(phone.value)) {
    error.value = PHONE_ERROR

    return false
  }

  if (password.value.length < 8) {
    error.value =
      'La contraseña debe tener al menos 8 caracteres.'

    return false
  }

  return true
}

// =====================================================
// PASO 1 -> PASO 2
// =====================================================

async function goToPhotoStep() {
  error.value = ''

  if (!validateForm()) {
    return
  }

  /*
   * IMPORTANTE:
   *
   * El CAPTCHA ya está montado de forma permanente.
   * No lo destruimos al cambiar de paso.
   */

  if (!recaptchaToken.value) {
    error.value =
      'Completa la verificación reCAPTCHA (marca la casilla "No soy un robot").'

    return
  }

  step.value = 'photo'

  await nextTick()

  if (
    availableStickers.value.length === 0
  ) {
    await loadStickers()
  }

  if (capturedDataUrl.value) {
    await renderPreview()
  } else {
    await startCamera()
  }
}

// =====================================================
// VOLVER
// =====================================================

function backToForm() {
  stopCamera()

  step.value = 'form'

  error.value = ''
}

// =====================================================
// Cámara
// =====================================================

async function startCamera() {
  error.value = ''

  try {
    if (
      !navigator.mediaDevices?.getUserMedia
    ) {
      throw new Error(
        'El navegador no permite acceder a la cámara.'
      )
    }

    stream.value =
      await navigator.mediaDevices.getUserMedia(
        {
          video: {
            facingMode: 'user',
            width: {
              ideal: 640
            },
            height: {
              ideal: 480
            }
          },
          audio: false
        }
      )

    await nextTick()

    if (video.value) {
      video.value.srcObject =
        stream.value

      await video.value.play()

      cameraActive.value = true
    }
  } catch (err) {
    error.value =
      err instanceof Error
        ? err.message
        : 'No fue posible acceder a la cámara.'
  }
}

function stopCamera() {
  if (stream.value) {
    stream.value
      .getTracks()
      .forEach(track =>
        track.stop()
      )

    stream.value = null
  }

  if (video.value) {
    video.value.srcObject = null
  }

  cameraActive.value = false
}

function capturePhoto() {
  error.value = ''

  if (
    !video.value ||
    !captureCanvas.value
  ) {
    error.value =
      'La cámara no está disponible.'

    return
  }

  const width =
    video.value.videoWidth

  const height =
    video.value.videoHeight

  if (!width || !height) {
    error.value =
      'La cámara todavía no está lista. Espera un momento.'

    return
  }

  captureCanvas.value.width =
    width

  captureCanvas.value.height =
    height

  const context =
    captureCanvas.value.getContext(
      '2d'
    )

  if (!context) {
    error.value =
      'No fue posible capturar la imagen.'

    return
  }

  context.drawImage(
    video.value,
    0,
    0,
    width,
    height
  )

  capturedDataUrl.value =
    captureCanvas.value.toDataURL(
      'image/jpeg',
      0.92
    )

  baseImage = null

  stopCamera()

  selectedFilter.value =
    filters[0].css

  placedStickers.value = []

  activeStickerKey.value =
    null

  nextTick(() =>
    renderPreview()
  )
}

function retakePhoto() {
  capturedDataUrl.value = ''

  baseImage = null

  placedStickers.value = []

  activeStickerKey.value =
    null

  startCamera()
}

// =====================================================
// Filtros
// =====================================================

function selectFilter(
  css: string
) {
  selectedFilter.value = css
}

function loadBaseImage(): Promise<HTMLImageElement> {
  return new Promise(resolve => {
    const img = new Image()

    img.onload = () => {
      baseImage = img
      resolve(img)
    }

    img.src =
      capturedDataUrl.value
  })
}

// =====================================================
// Preview
// =====================================================

async function renderPreview() {
  if (
    !capturedDataUrl.value ||
    !editCanvas.value
  ) {
    return
  }

  if (!baseImage) {
    await loadBaseImage()
  }

  const img = baseImage

  const canvas =
    editCanvas.value

  if (!img || !canvas) {
    return
  }

  canvas.width = img.width
  canvas.height = img.height

  const ctx =
    canvas.getContext('2d')

  if (!ctx) {
    return
  }

  ctx.clearRect(
    0,
    0,
    canvas.width,
    canvas.height
  )

  ctx.filter =
    selectedFilter.value

  ctx.drawImage(
    img,
    0,
    0
  )

  ctx.filter = 'none'

  for (
    const sticker of placedStickers.value
  ) {
    const stickerImg =
      stickerImageCache.get(
        sticker.src
      )

    if (!stickerImg) {
      continue
    }

    const w =
      canvas.width *
      sticker.scale

    const h =
      w *
      (
        stickerImg.height /
        stickerImg.width
      )

    const cx =
      sticker.x *
      canvas.width

    const cy =
      sticker.y *
      canvas.height

    ctx.drawImage(
      stickerImg,
      cx - w / 2,
      cy - h / 2,
      w,
      h
    )

    if (
      sticker.key ===
      activeStickerKey.value
    ) {
      ctx.save()

      ctx.strokeStyle =
        'rgba(255,255,255,0.9)'

      ctx.lineWidth =
        Math.max(
          2,
          canvas.width * 0.004
        )

      ctx.setLineDash([
        6,
        4
      ])

      ctx.strokeRect(
        cx - w / 2,
        cy - h / 2,
        w,
        h
      )

      ctx.restore()
    }
  }
}

watch(
  selectedFilter,
  () => {
    renderPreview()
  }
)

// =====================================================
// Base64
// =====================================================

function base64FromDataUrl(
  dataUrl: string
): string {
  return (
    dataUrl.split(',')[1] ?? ''
  )
}

// =====================================================
// REGISTRO
// =====================================================

async function register() {
  error.value = ''
  success.value = ''

  if (!capturedDataUrl.value) {
    error.value =
      'Toma una fotografía antes de continuar.'

    return
  }

  // ===================================================
  // CAPTCHA
  // ===================================================

  if (!recaptchaToken.value) {
    error.value =
      'Completa la verificación reCAPTCHA (marca la casilla "No soy un robot").'

    return
  }

  loading.value = true

  try {
    await renderPreview()

    const modifiedDataUrl =
      editCanvas.value?.toDataURL(
        'image/jpeg',
        0.92
      ) ??
      capturedDataUrl.value

    const response =
      await apiFetch<{ token: string }>(
        '/Auth/register',
        {
          method: 'POST',

          body: JSON.stringify({
            email: email.value,
            phone: phone.value,
            birthDate:
              birthDate.value,
            nickname:
              nickname.value,
            password:
              password.value,

            notificationMethod:
              notificationMethod.value,

            photoOriginalBase64:
              base64FromDataUrl(
                capturedDataUrl.value
              ),

            photoModifiedBase64:
              base64FromDataUrl(
                modifiedDataUrl
              ),

            recaptchaToken:
              recaptchaToken.value
          })
        }
      )

    localStorage.setItem(
      'token',
      response.token
    )

    success.value =
      '¡Cuenta creada! Te enviamos tu credencial con código QR. Redirigiendo...'

    setTimeout(() => {
      navigateTo('/dashboard')
    }, 1400)

  } catch (err) {
    error.value =
      err instanceof Error
        ? err.message
        : 'No fue posible completar el registro.'

    /*
     * Si el backend rechazó la solicitud,
     * reiniciamos el CAPTCHA.
     */
    resetRecaptcha()

  } finally {
    loading.value = false
  }
}

// =====================================================
// INICIALIZACIÓN
// =====================================================

onMounted(async () => {
  await nextTick()

  /*
   * El CAPTCHA se renderiza UNA SOLA VEZ.
   *
   * Como los pasos usan v-show en lugar de v-if,
   * este elemento nunca se destruye.
   */
  if (recaptchaEl.value) {
    await renderRecaptcha(
      recaptchaEl.value
    )
  }

  await loadStickers()
})

onBeforeUnmount(() => {
  stopCamera()
})
</script>

<template>
<ClientOnly>
  <main class="register-page">
    <section
      class="panel"
      :class="{ wide: step === 'photo' }"
    >
      <!-- ================================================= -->
      <!-- MARCA -->
      <!-- ================================================= -->

      <div class="brand">
        <span class="brand-mark">
          L
        </span>

        <div>
          <h1>Léxica</h1>

          <p>
            Analiza. Descubre. Comprende.
          </p>
        </div>
      </div>

      <!-- =================================================== -->
      <!-- RECAPTCHA PERMANENTE -->
      <!-- =================================================== -->
      <ClientOnly>
        <div class="recaptcha-wrap">
          <div ref="recaptchaEl"></div>
        </div>
      </ClientOnly>

      <!-- ================================================= -->
      <!-- PASO 1 -->
      <!-- IMPORTANTE: v-show, NO v-if -->
      <!-- ================================================= -->

      <div
        v-show="step === 'form'"
        class="step-content"
      >
        <h2>
          Crear cuenta
        </h2>

        <p class="subtitle">
          Regístrate para comenzar a
          analizar tus textos.
        </p>

        <form
          @submit.prevent="
            goToPhotoStep
          "
        >
          <label>
            Nickname

            <input
              v-model="nickname"
              type="text"
              required
              placeholder="Tu nombre de usuario"
            >
          </label>

          <label>
            Correo electrónico

            <input
              v-model="email"
              type="email"
              required
              placeholder="usuario@correo.com"
            >
          </label>

          <label>
            Teléfono

            <input
              v-model="phone"
              type="tel"
              inputmode="tel"
              maxlength="20"
              :pattern="PHONE_PATTERN"
              title="Solo números (se permite + al inicio, espacios o guiones)"
              @input="phone = sanitizePhoneInput(phone)"
              required
              placeholder="5555-5555"
            >
          </label>

          <label>
            Fecha de nacimiento

            <input
              v-model="birthDate"
              type="date"
              required
            >
          </label>

          <label>
            Contraseña

            <input
              v-model="password"
              type="password"
              required
              minlength="8"
              placeholder="Mínimo 8 caracteres"
            >
          </label>

          <fieldset
            class="notification-fieldset"
          >
            <legend>
              ¿Cómo quieres recibir tu
              credencial y notificaciones?
            </legend>

            <label
              class="radio-option"
            >
              <input
                v-model="
                  notificationMethod
                "
                type="radio"
                value="EMAIL"
              >

              Correo electrónico
            </label>

            <label
              class="radio-option"
            >
              <input
                v-model="
                  notificationMethod
                "
                type="radio"
                value="WHATSAPP"
              >

              WhatsApp
            </label>

            <label
              class="radio-option"
            >
              <input
                v-model="
                  notificationMethod
                "
                type="radio"
                value="BOTH"
              >

              Ambos
            </label>
          </fieldset>

          <div
            v-if="error"
            class="message error"
          >
            {{ error }}
          </div>

          <button
            type="submit"
          >
            Continuar con mi foto →
          </button>
        </form>

        <p class="footer-link">
          ¿Ya tienes cuenta?

          <NuxtLink to="/">
            Inicia sesión
          </NuxtLink>
        </p>
      </div>

      <!-- ================================================= -->
      <!-- PASO 2 -->
      <!-- IMPORTANTE: v-show, NO v-if -->
      <!-- ================================================= -->

      <div
        v-show="step === 'photo'"
        class="step-content"
      >
        <button
          type="button"
          class="back-link"
          @click="backToForm"
        >
          ← Editar mis datos
        </button>

        <h2>
          Foto de perfil
        </h2>

        <p class="subtitle">
          La usaremos para el reconocimiento
          facial y tu credencial. Puedes
          aplicarle un filtro y varios stickers
          antes de crear tu cuenta.
        </p>

        <!-- ================================================= -->
        <!-- CÁMARA -->
        <!-- ================================================= -->

        <div
          v-if="!capturedDataUrl"
          class="camera-container"
        >
          <video
            ref="video"
            autoplay
            playsinline
            muted
            class="camera"
          />

          <div
            v-if="!cameraActive"
            class="camera-placeholder"
          >
            <AppIcon name="camara" :size="56" />

            <p>
              Activando cámara...
            </p>
          </div>
        </div>

        <!-- ================================================= -->
        <!-- EDITOR -->
        <!-- ================================================= -->

        <div
          v-else
          class="edit-container"
        >
          <canvas
            ref="editCanvas"
            class="edit-canvas"
            @pointerdown="
              onCanvasPointerDown
            "
            @pointermove="
              onCanvasPointerMove
            "
            @pointerup="
              onCanvasPointerUp
            "
            @pointercancel="
              onCanvasPointerUp
            "
          />
        </div>

        <canvas
          ref="captureCanvas"
          class="hidden-canvas"
        />

        <!-- ================================================= -->
        <!-- STICKER LOCAL -->
        <!-- ================================================= -->

        <input
          ref="localStickerInput"
          type="file"
          accept="image/*"
          class="hidden-canvas"
          @change="
            onLocalStickerSelected
          "
        >

        <!-- ================================================= -->
        <!-- TOMAR FOTO -->
        <!-- ================================================= -->

        <div
          v-if="!capturedDataUrl"
          class="camera-actions"
        >
          <button
            type="button"
            class="primary"
            :disabled="!cameraActive"
            @click="capturePhoto"
          >
            <AppIcon name="camara" /> Tomar foto
          </button>
        </div>

        <!-- ================================================= -->
        <!-- EDITOR -->
        <!-- ================================================= -->

        <div
          v-else
          class="editor-content"
        >
          <div
            class="customize-section"
          >
            <p
              class="customize-label"
            >
              Filtro
            </p>

            <div
              class="filter-row"
            >
              <button
                v-for="f in filters"
                :key="f.id"
                type="button"
                class="chip"
                :class="{
                  active:
                    selectedFilter ===
                    f.css
                }"
                @click="
                  selectFilter(f.css)
                "
              >
                {{ f.label }}
              </button>
            </div>

            <p
              class="customize-label"
            >
              Stickers

              <span class="hint">
                (toca para agregar,
                arrastra en la foto
                para moverlo)
              </span>
            </p>

            <div
              class="filter-row"
            >
              <button
                v-for="s in availableStickers"
                :key="s.fileName"
                type="button"
                class="sticker-thumb"
                @click="
                  addSticker(s.url)
                "
              >
                <img
                  :src="s.url"
                  :alt="s.fileName"
                >
              </button>

              <button
                type="button"
                class="sticker-thumb local"
                @click="
                  triggerLocalSticker
                "
              >
                <AppIcon name="imagen" :size="28" />

                <span>
                  Mío
                </span>
              </button>
            </div>

            <p
              v-if="
                availableStickers.length ===
                0
              "
              class="hint"
            >
              Todavía no hay stickers
              cargados.
            </p>

            <div
              v-if="
                placedStickers.length >
                0
              "
              class="sticker-tools"
            >
              <label
                class="sticker-size"
              >
                Tamaño

                <input
                  type="range"
                  min="0.1"
                  max="0.6"
                  step="0.01"
                  :value="
                    activeScale()
                  "
                  @input="
                    setActiveScale(
                      Number(
                        (
                          $event.target as
                          HTMLInputElement
                        ).value
                      )
                    )
                  "
                >
              </label>

              <button
                type="button"
                class="chip"
                @click="
                  removeActiveSticker
                "
              >
                Quitar seleccionado
              </button>

              <button
                type="button"
                class="chip"
                @click="
                  clearStickers
                "
              >
                Quitar todos
              </button>
            </div>
          </div>

          <!-- ================================================= -->
          <!-- MENSAJES -->
          <!-- ================================================= -->

          <div
            v-if="error"
            class="message error"
          >
            {{ error }}
          </div>

          <div
            v-if="success"
            class="message success"
          >
            {{ success }}
          </div>

          <!-- ================================================= -->
          <!-- BOTONES -->
          <!-- ================================================= -->

          <div
            class="camera-actions"
          >
            <button
              type="button"
              class="secondary"
              :disabled="loading"
              @click="
                retakePhoto
              "
            >
              <AppIcon name="camara" /> Volver a tomar
            </button>

            <button
              type="button"
              class="primary"
              :disabled="loading"
              @click="register"
            >
              {{
                loading
                  ? 'Creando cuenta...'
                  : '✓ Confirmar y crear cuenta'
              }}
            </button>
          </div>
        </div>
      </div>
    </section>
  </main>
  </ClientOnly>
</template>

<style scoped>
.register-page {
  min-height: 100vh;

  display: grid;
  place-items: center;

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

  padding: 40px 16px;
}

.panel {
  width: min(420px, 100%);

  padding: 32px;

  border: 1px solid
    rgba(73, 151, 208, 0.16);

  border-radius: 20px;

  background:
    rgba(10, 21, 40, 0.75);

  box-shadow:
    0 25px 70px
    rgba(0, 0, 0, 0.22);

  transition:
    width 0.2s ease;
}

.panel.wide {
  width: min(480px, 100%);
}

.brand {
  display: flex;

  align-items: center;

  gap: 12px;

  margin-bottom: 24px;
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
    1px solid
    rgba(73, 151, 208, 0.35);

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

h2 {
  margin: 0 0 4px;

  font-size: 22px;
}

.subtitle {
  margin: 0 0 22px;

  color: #819ab9;

  font-size: 13px;

  line-height: 1.5;
}

.step-content {
  width: 100%;
}

form {
  display: flex;

  flex-direction: column;

  gap: 14px;
}

label {
  display: flex;

  flex-direction: column;

  gap: 6px;

  font-size: 12px;

  font-weight: 600;

  color: #86a5c8;
}

input[type="text"],
input[type="email"],
input[type="tel"],
input[type="date"],
input[type="password"] {
  padding: 12px;

  border:
    1px solid
    rgba(73, 151, 208, 0.25);

  border-radius: 10px;

  background: #0a162a;

  color: #eaf1fb;

  outline: none;

  font: inherit;
}

.notification-fieldset {
  border:
    1px solid
    rgba(73, 151, 208, 0.25);

  border-radius: 10px;

  padding: 12px;

  display: flex;

  flex-direction: column;

  gap: 8px;
}

.notification-fieldset legend {
  padding: 0 6px;

  font-size: 12px;

  font-weight: 600;

  color: #86a5c8;
}

.radio-option {
  flex-direction: row;

  align-items: center;

  gap: 8px;

  font-weight: 500;

  color: #cddcee;
}

.radio-option input {
  accent-color: #4997d0;
}

button {
  border: 0;

  border-radius: 11px;

  cursor: pointer;

  font-weight: 700;

  font: inherit;
}

button[type="submit"],
.primary {
  padding: 13px;

  background:
    linear-gradient(
      135deg,
      #2f6fb8,
      #4997d0
    );

  color: white;
}

.secondary {
  padding: 13px;

  background:
    rgba(73, 151, 208, 0.12);

  color: #cddcee;
}

button:disabled {
  opacity: 0.45;

  cursor: not-allowed;
}

.message {
  padding: 10px 12px;

  border-radius: 10px;

  font-size: 12px;
}

.message.error {
  border:
    1px solid
    rgba(248, 113, 113, 0.3);

  background:
    rgba(127, 29, 29, 0.15);

  color: #fca5a5;
}

.message.success {
  border:
    1px solid
    rgba(74, 222, 128, 0.2);

  background:
    rgba(22, 101, 52, 0.14);

  color: #86efac;
}

.footer-link {
  margin: 20px 0 0;

  text-align: center;

  font-size: 12px;

  color: #6682a7;
}

.footer-link a {
  color: #6faad8;

  font-weight: 600;
}

.back-link {
  background: none;

  border: 0;

  color: #6faad8;

  font-size: 12px;

  font-weight: 600;

  padding: 0 0 14px;

  cursor: pointer;
}

/* =====================================================
   RECAPTCHA
   ===================================================== */

.recaptcha-wrap {
  display: flex;

  justify-content: center;

  align-items: center;

  width: 100%;

  min-height: 78px;

  margin: 4px 0 18px;

  overflow: visible;

  position: relative;

  z-index: 5;
}

.recaptcha-wrap > div {
  min-height: 78px;
}

/* =====================================================
   CÁMARA
   ===================================================== */

.camera-container,
.edit-container {
  position: relative;

  width: 100%;

  aspect-ratio: 4 / 3;

  overflow: hidden;

  border-radius: 16px;

  background: #050b16;

  margin-bottom: 14px;
}

.camera {
  width: 100%;
  height: 100%;

  object-fit: cover;

  transform: scaleX(-1);
}

.edit-canvas {
  width: 100%;
  height: 100%;

  object-fit: cover;

  touch-action: none;
}

.camera-placeholder {
  position: absolute;

  inset: 0;

  display: flex;

  flex-direction: column;

  gap: 8px;

  justify-content: center;

  align-items: center;

  color: #9cc6ea;

  font-size: 2rem;
}

.camera-placeholder p {
  font-size: 12px;

  margin: 0;
}

.hidden-canvas {
  display: none;
}

.camera-actions {
  display: grid;

  grid-template-columns:
    1fr 1fr;

  gap: 10px;

  margin-top: 6px;
}

/* =====================================================
   EDITOR
   ===================================================== */

.customize-section {
  margin-bottom: 14px;
}

.customize-label {
  margin: 10px 0 6px;

  font-size: 12px;

  font-weight: 600;

  color: #86a5c8;
}

.customize-label .hint {
  font-weight: 400;

  color: #6682a7;
}

.hint {
  font-size: 11px;

  color: #6682a7;

  margin: 4px 0;
}

.filter-row {
  display: flex;

  flex-wrap: wrap;

  gap: 8px;
}

.chip {
  padding: 7px 12px;

  border-radius: 999px;

  background: #0a162a;

  border:
    1px solid
    rgba(73, 151, 208, 0.3);

  color: #cddcee;

  font-size: 12px;

  font-weight: 600;
}

.chip.active {
  background:
    linear-gradient(
      135deg,
      #2f6fb8,
      #4997d0
    );

  color: white;

  border-color: transparent;
}

.sticker-thumb {
  width: 44px;
  height: 44px;

  border-radius: 10px;

  background: #0a162a;

  border:
    1px solid
    rgba(73, 151, 208, 0.3);

  padding: 4px;

  display: grid;

  place-items: center;

  overflow: hidden;
}

.sticker-thumb img {
  width: 100%;
  height: 100%;

  object-fit: contain;
}

.sticker-thumb.local {
  flex-direction: column;

  font-size: 15px;

  color: #9cc6ea;

  gap: 2px;
}

.sticker-thumb.local span {
  font-size: 8px;

  font-weight: 700;
}

.sticker-tools {
  display: flex;

  flex-wrap: wrap;

  align-items: center;

  gap: 8px;

  margin-top: 10px;
}

.sticker-size {
  display: flex;

  align-items: center;

  gap: 6px;

  font-size: 11px;

  color: #86a5c8;
}

@media (max-width: 480px) {
  .panel,
  .panel.wide {
    width: 100%;

    padding: 24px;
  }

  .camera-actions {
    grid-template-columns: 1fr;
  }
}
</style>