<script setup lang="ts">
import { ref, onMounted, onBeforeUnmount } from 'vue'
import jsQR from 'jsqr'
import { apiFetch } from '~/services/api'

const route = useRoute()
const router = useRouter()

const video = ref<HTMLVideoElement | null>(null)
const canvas = ref<HTMLCanvasElement | null>(null)
const stream = ref<MediaStream | null>(null)
const cameraActive = ref(false)

const imageInput = ref<HTMLInputElement | null>(null)
const loading = ref(false)
const error = ref('')

interface Identity { nickname: string, roleName: string, photoUrl: string }
const identity = ref<Identity | null>(null)

let animationFrameId: number | null = null

// El QR de la credencial codifica una URL completa (.../auth/qr?token=...)
// para poder abrirse con la cámara nativa del teléfono. Si lo que se leyó
// (por cámara, pegado a mano, o el query param de esta misma página) es
// esa URL, se extrae solo el token; si no, se usa el valor tal cual.
function extractToken(raw: string): string {
  const value = raw.trim()

  try {
    const url = new URL(value)
    const token = url.searchParams.get('token')
    if (token) return token
  } catch {
    // No era una URL, se usa el valor tal cual.
  }

  return value
}

onMounted(() => {
  const tokenFromLink = route.query.token

  if (typeof tokenFromLink === 'string' && tokenFromLink) {
    // El token es un secreto: se quita de la barra de direcciones (y del
    // historial) en cuanto se lee, y no se muestra en el campo manual.
    router.replace({ query: {} })
    submitToken(tokenFromLink)
  }
})

async function startCamera() {
  error.value = ''

  try {
    if (!navigator.mediaDevices?.getUserMedia) {
      throw new Error('El navegador no permite acceder a la cámara.')
    }

    stream.value = await navigator.mediaDevices.getUserMedia({
      video: { facingMode: 'environment', width: { ideal: 640 }, height: { ideal: 480 } },
      audio: false
    })

    if (video.value) {
      video.value.srcObject = stream.value
      await video.value.play()
      cameraActive.value = true
      scanLoop()
    }
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'No fue posible acceder a la cámara.'
  }
}

function stopCamera() {
  if (animationFrameId !== null) {
    cancelAnimationFrame(animationFrameId)
    animationFrameId = null
  }

  if (stream.value) {
    stream.value.getTracks().forEach(track => track.stop())
    stream.value = null
  }

  if (video.value) {
    video.value.srcObject = null
  }

  cameraActive.value = false
}

function scanLoop() {
  if (!cameraActive.value || !video.value || !canvas.value) {
    return
  }

  const width = video.value.videoWidth
  const height = video.value.videoHeight

  if (width && height) {
    canvas.value.width = width
    canvas.value.height = height

    const ctx = canvas.value.getContext('2d', { willReadFrequently: true })

    if (ctx) {
      ctx.drawImage(video.value, 0, 0, width, height)

      const imageData = ctx.getImageData(0, 0, width, height)
      const code = jsQR(imageData.data, width, height)

      if (code?.data) {
        stopCamera()
        submitToken(code.data)
        return
      }
    }
  }

  animationFrameId = requestAnimationFrame(scanLoop)
}

async function submitToken(value: string) {
  error.value = ''

  const token = extractToken(value)

  if (!token) {
    error.value = 'Ingresa o escanea el código de tu credencial.'
    return
  }

  loading.value = true

  try {
    const response = await apiFetch<{ token: string }>('/Auth/qr', {
      method: 'POST',
      body: JSON.stringify({ qrCodeValue: token })
    })

    localStorage.setItem('token', response.token)

    // Muestra quién es antes de entrar (así el QR sirve también para
    // verificar identidad, no solo para loguearse).
    try {
      const profile = await apiFetch<{ nickname: string, roleName: string }>('/Profile/me')
      const photoUrl = (await fetchAuthedObjectUrl('/Profile/photo/modified')) ?? ''
      identity.value = { nickname: profile.nickname, roleName: profile.roleName, photoUrl }
    } catch {
      // Si esto falla no es grave: se continúa igual al panel.
      goToDashboard()
    }
  } catch (err) {
    error.value =
      err instanceof Error ? err.message : 'No fue posible iniciar sesión con ese código.'
  } finally {
    loading.value = false
  }
}

// Alternativa sin cámara: leer el QR desde una foto o captura de la credencial.
function pickImage() {
  imageInput.value?.click()
}

async function onImageSelected(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file) return

  error.value = ''

  try {
    const bitmap = await createImageBitmap(file)
    const scale = Math.min(1, 1600 / Math.max(bitmap.width, bitmap.height))
    const width = Math.round(bitmap.width * scale)
    const height = Math.round(bitmap.height * scale)

    const work = document.createElement('canvas')
    work.width = width
    work.height = height

    const ctx = work.getContext('2d', { willReadFrequently: true })
    if (!ctx) throw new Error()

    ctx.drawImage(bitmap, 0, 0, width, height)
    const code = jsQR(ctx.getImageData(0, 0, width, height).data, width, height)

    if (!code?.data) {
      error.value = 'No se encontró un código QR en la imagen. Prueba con una imagen más nítida o recortada al QR.'
      return
    }

    stopCamera()
    await submitToken(code.data)
  } catch {
    error.value = 'No fue posible leer la imagen.'
  }
}

async function goToDashboard() {
  await navigateTo('/dashboard')
}

onBeforeUnmount(() => {
  stopCamera()
})
</script>

<template>
  <main class="qr-page">
    <section class="panel">
      <div class="brand">
        <span class="brand-mark">L</span>
        <div>
          <h1>Léxica</h1>
          <p>Analiza. Descubre. Comprende.</p>
        </div>
      </div>

      <template v-if="!identity">
        <h2>Login por QR</h2>
        <p class="subtitle">
          Escanea el código QR de tu credencial con la cámara, o sube una imagen donde se vea.
        </p>

        <div class="camera-container">
          <video ref="video" autoplay playsinline muted class="camera" />

          <div v-if="cameraActive" class="qr-guide">
            <div class="qr-frame"></div>
            <span>Apunta al código QR</span>
          </div>

          <div v-if="!cameraActive" class="camera-placeholder">
            <AppIcon name="escanear-qr" :size="56" />
            <p>Activa la cámara para escanear</p>
          </div>
        </div>

        <canvas ref="canvas" class="hidden-canvas" />

        <div class="actions">
          <button v-if="!cameraActive" type="button" class="primary" :disabled="loading" @click="startCamera">
            <AppIcon name="escanear-qr" /> Activar cámara y escanear
          </button>
          <button v-else type="button" class="secondary" :disabled="loading" @click="stopCamera">
            Apagar cámara
          </button>
        </div>

        <div class="divider"><span>o</span></div>

        <button type="button" class="secondary full" :disabled="loading" @click="pickImage">
          <AppIcon name="imagen" /> {{ loading ? 'Verificando...' : 'Subir imagen del QR' }}
        </button>
        <p class="hint">Sin cámara: sube una foto o captura de tu credencial donde se vea el QR.</p>
        <input ref="imageInput" type="file" accept="image/*" hidden @change="onImageSelected">

        <div v-if="error" class="message error">{{ error }}</div>

        <p class="footer-link">
          <NuxtLink to="/">← Volver al login con contraseña</NuxtLink>
        </p>
      </template>

      <template v-else>
        <h2>Identidad verificada</h2>
        <p class="subtitle">Esta credencial corresponde a:</p>

        <div class="identity-card">
          <img v-if="identity.photoUrl" :src="identity.photoUrl" alt="Foto" class="identity-photo">
          <div v-else class="identity-photo placeholder">{{ identity.nickname.charAt(0).toUpperCase() }}</div>
          <h3>{{ identity.nickname }}</h3>
          <span class="identity-role">{{ identity.roleName }}</span>
        </div>

        <button type="button" class="primary full" @click="goToDashboard">
          Continuar al panel →
        </button>
      </template>
    </section>
  </main>
</template>

<style scoped>
.qr-page {
  min-height: 100vh;
  display: grid;
  place-items: center;
  background:
    linear-gradient(180deg, rgba(6, 12, 28, 0.55) 0%, rgba(6, 12, 28, 0.78) 100%),
    url('/images/loginpage.png') center / cover no-repeat fixed,
    #07101f;
  color: #eaf1fb;
  font-family: 'Poppins', 'Segoe UI', system-ui, sans-serif;
  padding: 40px 16px;
}

.panel {
  width: min(420px, 100%);
  padding: 32px;
  border: 1px solid rgba(73, 151, 208, 0.16);
  border-radius: 20px;
  background: rgba(10, 21, 40, 0.75);
  box-shadow: 0 25px 70px rgba(0, 0, 0, 0.22);
}

.brand { display: flex; align-items: center; gap: 12px; margin-bottom: 24px; }
.brand-mark {
  width: 40px; height: 40px; display: grid; place-items: center;
  border-radius: 12px;
  background: linear-gradient(135deg, rgba(73, 151, 208, 0.3), rgba(73, 151, 208, 0.05));
  border: 1px solid rgba(73, 151, 208, 0.35);
  color: #9cc6ea; font-weight: 800;
}
.brand h1 { margin: 0; font-size: 19px; }
.brand p { margin: 1px 0 0; font-size: 10px; color: #6682a7; }

h2 { margin: 0 0 4px; font-size: 22px; }
.subtitle { margin: 0 0 20px; color: #819ab9; font-size: 13px; line-height: 1.5; }

.camera-container {
  position: relative;
  width: 100%;
  aspect-ratio: 4 / 3;
  overflow: hidden;
  border-radius: 16px;
  background: #050b16;
  margin-bottom: 14px;
}

.camera { width: 100%; height: 100%; object-fit: cover; }

.camera-placeholder {
  position: absolute; inset: 0;
  display: flex; flex-direction: column; gap: 8px;
  justify-content: center; align-items: center;
  color: #9cc6ea; font-size: 2rem;
}
.camera-placeholder p { font-size: 12px; margin: 0; }

.qr-guide {
  position: absolute; inset: 0;
  display: flex; flex-direction: column;
  justify-content: center; align-items: center;
  pointer-events: none;
}
.qr-frame {
  width: 55%; aspect-ratio: 1 / 1;
  border: 3px solid rgba(255, 255, 255, 0.85);
  border-radius: 16px;
  box-shadow: 0 0 0 9999px rgba(0, 0, 0, 0.28);
}
.qr-guide span {
  margin-top: 10px; padding: 4px 10px; border-radius: 999px;
  background: rgba(0, 0, 0, 0.6); color: white; font-size: 11px;
}

.hidden-canvas { display: none; }

.identity-card {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 10px;
  padding: 24px;
  margin-bottom: 18px;
  border-radius: 16px;
  border: 1px solid rgba(73, 151, 208, 0.25);
  background: #0a162a;
}

.identity-photo {
  width: 96px;
  height: 96px;
  border-radius: 50%;
  object-fit: cover;
  border: 3px solid #4997d0;
}

.identity-photo.placeholder {
  display: grid;
  place-items: center;
  background: linear-gradient(135deg, #2f6fb8, #4997d0);
  color: white;
  font-size: 32px;
  font-weight: 800;
}

.identity-card h3 { margin: 4px 0 0; font-size: 18px; }

.identity-role {
  padding: 3px 12px;
  border-radius: 999px;
  background: rgba(73, 151, 208, 0.15);
  color: #9cc6ea;
  font-size: 11px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.actions { display: flex; }

button {
  border: 0; border-radius: 11px; padding: 13px; cursor: pointer;
  font-weight: 700; font: inherit; width: 100%;
}
.primary { background: linear-gradient(135deg, #2f6fb8, #4997d0); color: white; }
.secondary { background: rgba(73, 151, 208, 0.12); color: #cddcee; }
.full { margin-top: 12px; }
button:disabled { opacity: 0.45; cursor: not-allowed; }

.hint { margin: 8px 0 0; text-align: center; font-size: 11px; color: #6682a7; }

.divider {
  display: flex; align-items: center; gap: 12px;
  margin: 18px 0; color: #4d6183; font-size: 12px;
}
.divider::before, .divider::after { content: ''; flex: 1; height: 1px; background: rgba(73, 151, 208, 0.2); }

.field {
  display: flex; flex-direction: column; gap: 6px;
  font-size: 12px; font-weight: 600; color: #86a5c8;
  margin-bottom: 4px;
}
.field input {
  padding: 12px;
  border: 1px solid rgba(73, 151, 208, 0.25);
  border-radius: 10px;
  background: #0a162a;
  color: #eaf1fb;
  outline: none;
  font: inherit;
}

.message {
  margin-top: 14px;
  padding: 10px 12px; border-radius: 10px; font-size: 12px;
}
.message.error {
  border: 1px solid rgba(248, 113, 113, 0.3);
  background: rgba(127, 29, 29, 0.15);
  color: #fca5a5;
}

.footer-link { margin: 20px 0 0; text-align: center; font-size: 12px; }
.footer-link a { color: #6faad8; font-weight: 600; text-decoration: none; }
</style>
