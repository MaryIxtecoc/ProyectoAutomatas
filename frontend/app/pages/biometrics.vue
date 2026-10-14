<script setup lang="ts">
import { computed, ref, onMounted, onBeforeUnmount } from 'vue'
import { apiFetch } from '~/services/api'

definePageMeta({ middleware: 'auth' })

const video = ref<HTMLVideoElement | null>(null)
const canvas = ref<HTMLCanvasElement | null>(null)
const stream = ref<MediaStream | null>(null)

const cameraActive = ref(false)
const loading = ref(false)
const registered = ref(false)

const message = ref('')
const errorMessage = ref('')

// A dónde volver: la página que abrió esta (?from=/ruta), o el perfil.
const route = useRoute()
const backTo = computed(() => {
  const from = route.query.from
  return typeof from === 'string' && from.startsWith('/') && !from.startsWith('//') ? from : '/perfil'
})

// Regreso automático tras registrar el rostro.
const redirectIn = ref(0)
let redirectTimer: ReturnType<typeof setInterval> | null = null

function startRedirect() {
  redirectIn.value = 4
  redirectTimer = setInterval(() => {
    redirectIn.value--
    if (redirectIn.value <= 0) {
      cancelRedirect()
      navigateTo(backTo.value)
    }
  }, 1000)
}

function cancelRedirect() {
  if (redirectTimer) clearInterval(redirectTimer)
  redirectTimer = null
  redirectIn.value = 0
}

async function loadBiometricStatus() {
  try {
    const result = await apiFetch<{ registered: boolean }>(
      '/Biometrics/me'
    )

    registered.value = result.registered
  } catch (error) {
    console.error(error)
  }
}

async function startCamera() {
  errorMessage.value = ''
  message.value = ''

  try {
    if (!navigator.mediaDevices?.getUserMedia) {
      throw new Error(
        'El navegador no permite acceder a la cámara.'
      )
    }

    stream.value = await navigator.mediaDevices.getUserMedia({
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
    })

    if (video.value) {
      video.value.srcObject = stream.value

      await video.value.play()

      cameraActive.value = true
    }
  } catch (error) {
    console.error(error)

    errorMessage.value =
      error instanceof Error
        ? error.message
        : 'No fue posible acceder a la cámara.'
  }
}

function stopCamera() {
  if (stream.value) {
    stream.value.getTracks().forEach(track => track.stop())
    stream.value = null
  }

  if (video.value) {
    video.value.srcObject = null
  }

  cameraActive.value = false
}

function captureImage(): Blob | null {
  if (!video.value || !canvas.value) {
    errorMessage.value = 'La cámara no está disponible.'
    return null
  }

  const width = video.value.videoWidth
  const height = video.value.videoHeight

  if (!width || !height) {
    errorMessage.value =
      'La cámara todavía no está lista. Espera un momento.'
    return null
  }

  canvas.value.width = width
  canvas.value.height = height

  const context = canvas.value.getContext('2d')

  if (!context) {
    errorMessage.value =
      'No fue posible capturar la imagen.'
    return null
  }

  context.drawImage(
    video.value,
    0,
    0,
    width,
    height
  )

  return canvas.value.toDataURL(
    'image/jpeg',
    0.9
  )
    ? dataUrlToBlob(
        canvas.value.toDataURL(
          'image/jpeg',
          0.9
        )
      )
    : null
}

function dataUrlToBlob(dataUrl: string): Blob {
  const parts = dataUrl.split(',')
  const mime = parts[0]
    .match(/:(.*?);/)?.[1] ?? 'image/jpeg'

  const binary = atob(parts[1])
  const bytes = new Uint8Array(binary.length)

  for (let i = 0; i < binary.length; i++) {
    bytes[i] = binary.charCodeAt(i)
  }

  return new Blob(
    [bytes],
    { type: mime }
  )
}

async function registerFace() {
  errorMessage.value = ''
  message.value = ''

  const image = captureImage()

  if (!image) {
    return
  }

  loading.value = true

  try {
    const formData = new FormData()

    formData.append(
      'image',
      image,
      'face.jpg'
    )

    const result = await apiFetch<{ message: string }>(
      '/Biometrics/face',
      {
        method: 'POST',
        body: formData
      }
    )

    registered.value = true
    message.value =
      result.message ||
      'Rostro registrado correctamente.'
    stopCamera()
    startRedirect()
  } catch (error) {
    console.error(error)

    errorMessage.value =
      error instanceof Error
        ? error.message
        : 'No fue posible registrar el rostro.'
  } finally {
    loading.value = false
  }
}

async function verifyFace() {
  errorMessage.value = ''
  message.value = ''

  const image = captureImage()

  if (!image) {
    return
  }

  loading.value = true

  try {
    const formData = new FormData()

    formData.append(
      'image',
      image,
      'face.jpg'
    )

    const result = await apiFetch<{
      verified: boolean
      similarity: number
      threshold: number
    }>(
      '/Biometrics/face/verify',
      {
        method: 'POST',
        body: formData
      }
    )

    if (result.verified) {
      message.value =
        `Identidad verificada correctamente. ` +
        `Similitud: ${(result.similarity * 100).toFixed(1)}%.`
    } else {
      errorMessage.value =
        `El rostro no coincide. ` +
        `Similitud: ${(result.similarity * 100).toFixed(1)}%.`
    }
  } catch (error) {
    console.error(error)

    errorMessage.value =
      error instanceof Error
        ? error.message
        : 'No fue posible verificar el rostro.'
  } finally {
    loading.value = false
  }
}

async function deleteFace() {
  errorMessage.value = ''
  message.value = ''

  loading.value = true

  try {
    const result = await apiFetch<{ message: string }>(
      '/Biometrics/face',
      {
        method: 'DELETE'
      }
    )

    registered.value = false

    message.value =
      result.message ||
      'Datos biométricos eliminados correctamente.'
  } catch (error) {
    console.error(error)

    errorMessage.value =
      error instanceof Error
        ? error.message
        : 'No fue posible eliminar el rostro.'
  } finally {
    loading.value = false
  }
}

onMounted(() => {
  loadBiometricStatus()
})

onBeforeUnmount(() => {
  stopCamera()
  cancelRedirect()
})
</script>

<template>
  <main class="biometrics-page">
    <header class="topbar">
      <div class="brand">
        <span class="brand-mark">L</span>
        <div>
          <h1>Léxica</h1>
          <p>Reconocimiento facial</p>
        </div>
      </div>
      <nav class="topbar-actions">
        <NuxtLink :to="backTo" class="link-button">← Volver</NuxtLink>
        <NuxtLink to="/dashboard" class="link-button">Análisis</NuxtLink>
      </nav>
    </header>

    <section class="panel">
      <div class="header">
        <div>
          <h2>Registro de rostro</h2>
          <p class="description">
            Registra tu rostro para iniciar sesión con reconocimiento facial.
          </p>
        </div>

        <span class="status" :class="registered ? 'registered' : 'not-registered'">
          <span class="status-dot" />
          {{ registered ? 'Rostro registrado' : 'Sin rostro registrado' }}
        </span>
      </div>

      <div class="instructions">
        <strong>Antes de comenzar</strong>
        <ul>
          <li>Coloca tu rostro frente a la cámara.</li>
          <li>Asegúrate de tener buena iluminación.</li>
          <li>Procura que solamente aparezca un rostro.</li>
          <li>Mira directamente hacia la cámara.</li>
        </ul>
      </div>

      <div class="camera-container">
        <video ref="video" autoplay playsinline muted class="camera" />

        <div v-if="cameraActive" class="face-guide">
          <div class="face-oval" />
          <span>Centra tu rostro</span>
        </div>

        <div v-if="!cameraActive" class="camera-placeholder">
          <AppIcon name="camara" :size="64" />
          <p>Activa la cámara para comenzar</p>
        </div>
      </div>

      <canvas ref="canvas" class="hidden-canvas" />

      <div class="camera-actions">
        <button v-if="!cameraActive" type="button" class="primary" :disabled="loading" @click="startCamera">
          <AppIcon name="camara" /> Activar cámara
        </button>
        <button v-else type="button" class="secondary" :disabled="loading" @click="stopCamera">
          Apagar cámara
        </button>
      </div>

      <div v-if="cameraActive" class="biometric-actions">
        <button type="button" class="register" :disabled="loading" @click="registerFace">
          {{ loading ? 'Procesando...' : registered ? 'Actualizar rostro' : 'Registrar rostro' }}
        </button>
        <button v-if="registered" type="button" class="verify" :disabled="loading" @click="verifyFace">
          Verificar identidad
        </button>
      </div>

      <div v-if="message" class="success-message">
        {{ message }}
        <div v-if="redirectIn > 0" class="redirect">
          Volviendo en {{ redirectIn }} s...
          <NuxtLink :to="backTo" class="inline-link">Volver ahora</NuxtLink>
          <button type="button" class="inline-link" @click="cancelRedirect">Quedarme aquí</button>
        </div>
      </div>

      <div v-if="errorMessage" class="error-message">{{ errorMessage }}</div>

      <div v-if="registered" class="registered-info">
        <strong>✓ Biometría activa</strong>
        <span>Tu rostro está registrado y puedes usarlo para iniciar sesión.</span>
      </div>

      <button v-if="registered" type="button" class="delete-button" :disabled="loading" @click="deleteFace">
        Eliminar registro facial
      </button>
    </section>
  </main>
</template>

<style scoped>
.biometrics-page {
  min-height: 100vh;
  padding: 24px;
  background:
    linear-gradient(180deg, rgba(6, 12, 28, 0.55) 0%, rgba(6, 12, 28, 0.78) 100%),
    url('/images/apartado2.png') center / cover no-repeat fixed,
    #07101f;
  color: #eaf1fb;
  font-family: 'Poppins', 'Segoe UI', system-ui, sans-serif;
}

.topbar { display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 14px; margin-bottom: 24px; }
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

.panel {
  width: min(640px, 100%); margin: 0 auto; padding: 26px; border-radius: 18px;
  background: rgba(10, 21, 40, 0.75); border: 1px solid rgba(73, 151, 208, 0.18);
  box-shadow: 0 25px 70px rgba(0, 0, 0, 0.22);
}

.header { display: flex; justify-content: space-between; align-items: flex-start; gap: 14px; flex-wrap: wrap; margin-bottom: 18px; }
.header h2 { margin: 0 0 4px; font-size: 20px; }
.description { margin: 0; color: #819ab9; font-size: 13px; }

.status { display: inline-flex; align-items: center; gap: 7px; padding: 5px 12px; border-radius: 999px; font-size: 11.5px; font-weight: 700; }
.status-dot { width: 8px; height: 8px; border-radius: 50%; background: currentColor; }
.status.registered { background: rgba(34, 197, 94, 0.15); color: #86efac; }
.status.not-registered { background: rgba(251, 146, 60, 0.15); color: #fdba74; }

.instructions {
  padding: 14px 16px; margin-bottom: 16px; border-radius: 12px;
  background: rgba(5, 11, 22, 0.55); border: 1px solid rgba(73, 151, 208, 0.12); font-size: 12.5px; color: #cddcee;
}
.instructions ul { margin: 8px 0 0; padding-left: 18px; color: #9fb4cf; }
.instructions li { margin-bottom: 3px; }

.camera-container {
  position: relative; width: 100%; aspect-ratio: 4 / 3; overflow: hidden; border-radius: 16px;
  background: #050b16; border: 1px solid rgba(73, 151, 208, 0.15); margin-bottom: 14px;
}
.camera { width: 100%; height: 100%; object-fit: cover; transform: scaleX(-1); }
.camera-placeholder {
  position: absolute; inset: 0; display: flex; flex-direction: column; gap: 10px;
  justify-content: center; align-items: center; color: #9cc6ea;
}
.camera-placeholder p { margin: 0; font-size: 12.5px; }
.face-guide { position: absolute; inset: 0; display: flex; flex-direction: column; justify-content: center; align-items: center; pointer-events: none; }
.face-oval { width: 46%; aspect-ratio: 3 / 4; border: 3px dashed rgba(156, 198, 234, 0.85); border-radius: 50%; }
.face-guide span { margin-top: 10px; padding: 4px 10px; border-radius: 999px; background: rgba(0, 0, 0, 0.6); font-size: 11px; }
.hidden-canvas { display: none; }

button { border: 0; border-radius: 11px; padding: 12px 16px; cursor: pointer; font: inherit; font-size: 13px; font-weight: 700; }
button:disabled { opacity: 0.45; cursor: not-allowed; }
.camera-actions { display: flex; justify-content: center; margin-bottom: 12px; }
.camera-actions button { display: inline-flex; align-items: center; gap: 8px; }
.primary { background: linear-gradient(135deg, #2f6fb8, #4997d0); color: white; }
.secondary { background: rgba(73, 151, 208, 0.12); color: #cddcee; }
.biometric-actions { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; margin-bottom: 12px; }
.biometric-actions button:only-child { grid-column: 1 / -1; }
.register { background: linear-gradient(135deg, #2f6fb8, #4997d0); color: white; }
.verify { background: rgba(73, 151, 208, 0.15); color: #cddcee; border: 1px solid rgba(73, 151, 208, 0.3); }

.success-message, .error-message, .registered-info { padding: 12px 14px; border-radius: 12px; font-size: 12.5px; margin-bottom: 12px; }
.success-message { background: rgba(34, 197, 94, 0.12); color: #86efac; border: 1px solid rgba(34, 197, 94, 0.25); }
.error-message { background: rgba(127, 29, 29, 0.15); color: #fca5a5; border: 1px solid rgba(248, 113, 113, 0.3); }
.registered-info { display: flex; flex-direction: column; gap: 4px; background: rgba(5, 11, 22, 0.55); border: 1px solid rgba(34, 197, 94, 0.2); color: #9fb4cf; }
.registered-info strong { color: #86efac; }
.redirect { display: flex; flex-wrap: wrap; gap: 10px; align-items: center; margin-top: 6px; color: #cddcee; }
.inline-link { background: none; border: 0; padding: 0; color: #6faad8; font-weight: 700; font-size: 12.5px; text-decoration: underline; cursor: pointer; }

.delete-button {
  width: 100%; background: rgba(248, 113, 113, 0.1); color: #fca5a5; border: 1px solid rgba(248, 113, 113, 0.3);
}

@media (max-width: 560px) {
  .biometrics-page { padding: 16px; }
  .panel { padding: 18px; }
  .biometric-actions { grid-template-columns: 1fr; }
}
</style>
