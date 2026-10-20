<script setup lang="ts">
import { ref, onBeforeUnmount } from 'vue'
import { apiFetch } from '~/services/api'

const email = ref('')

const video = ref<HTMLVideoElement | null>(null)
const canvas = ref<HTMLCanvasElement | null>(null)
const stream = ref<MediaStream | null>(null)
const cameraActive = ref(false)

const loading = ref(false)
const error = ref('')

async function startCamera() {
  error.value = ''

  if (!email.value) {
    error.value = 'Ingresa tu correo antes de activar la cámara.'
    return
  }

  try {
    if (!navigator.mediaDevices?.getUserMedia) {
      throw new Error('El navegador no permite acceder a la cámara.')
    }

    stream.value = await navigator.mediaDevices.getUserMedia({
      video: { facingMode: 'user', width: { ideal: 640 }, height: { ideal: 480 } },
      audio: false
    })

    if (video.value) {
      video.value.srcObject = stream.value
      await video.value.play()
      cameraActive.value = true
    }
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'No fue posible acceder a la cámara.'
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

function captureBase64(): string | null {
  if (!video.value || !canvas.value) {
    error.value = 'La cámara no está disponible.'
    return null
  }

  const width = video.value.videoWidth
  const height = video.value.videoHeight

  if (!width || !height) {
    error.value = 'La cámara todavía no está lista. Espera un momento.'
    return null
  }

  canvas.value.width = width
  canvas.value.height = height

  const context = canvas.value.getContext('2d')
  if (!context) {
    error.value = 'No fue posible capturar la imagen.'
    return null
  }

  context.drawImage(video.value, 0, 0, width, height)

  const dataUrl = canvas.value.toDataURL('image/jpeg', 0.92)
  return dataUrl.split(',')[1] ?? null
}

async function loginWithFace() {
  error.value = ''

  const photoBase64 = captureBase64()
  if (!photoBase64) {
    return
  }

  loading.value = true

  try {
    const response = await apiFetch<{ token: string }>('/Auth/facial', {
      method: 'POST',
      body: JSON.stringify({
        email: email.value,
        photoBase64
      })
    })

    localStorage.setItem('token', response.token)

    await navigateTo('/dashboard')
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'No fue posible iniciar sesión.'
  } finally {
    loading.value = false
  }
}

onBeforeUnmount(() => {
  stopCamera()
})
</script>

<template>
  <main class="facial-page">
    <section class="panel">
      <div class="brand">
        <span class="brand-mark">L</span>
        <div>
          <h1>Léxica</h1>
          <p>Analiza. Descubre. Comprende.</p>
        </div>
      </div>

      <h2>Login facial</h2>
      <p class="subtitle">
        Ingresa tu correo, activa la cámara y verifica tu identidad con tu rostro.
      </p>

      <label class="field">
        Correo electrónico
        <input v-model="email" type="email" placeholder="usuario@correo.com" :disabled="cameraActive">
      </label>

      <div class="camera-container">
        <video ref="video" autoplay playsinline muted class="camera" />

        <div v-if="!cameraActive" class="camera-placeholder">
          <AppIcon name="camara" :size="56" />
          <p>Activa la cámara para continuar</p>
        </div>
      </div>

      <canvas ref="canvas" class="hidden-canvas" />

      <div v-if="error" class="message error">{{ error }}</div>

      <div class="actions">
        <button v-if="!cameraActive" type="button" class="primary" @click="startCamera">
          <AppIcon name="camara" /> Activar cámara
        </button>

        <template v-else>
          <button type="button" class="secondary" :disabled="loading" @click="stopCamera">
            Apagar cámara
          </button>
          <button type="button" class="primary" :disabled="loading" @click="loginWithFace">
            {{ loading ? 'Verificando...' : 'Verificar e iniciar sesión' }}
          </button>
        </template>
      </div>

      <p class="footer-link">
        <NuxtLink to="/">← Volver al login con contraseña</NuxtLink>
      </p>
    </section>
  </main>
</template>

<style scoped>
.facial-page {
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

.field {
  display: flex; flex-direction: column; gap: 6px;
  font-size: 12px; font-weight: 600; color: #86a5c8;
  margin-bottom: 14px;
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

.camera-container {
  position: relative;
  width: 100%;
  aspect-ratio: 4 / 3;
  overflow: hidden;
  border-radius: 16px;
  background: #050b16;
  margin-bottom: 14px;
}

.camera { width: 100%; height: 100%; object-fit: cover; transform: scaleX(-1); }

.camera-placeholder {
  position: absolute; inset: 0;
  display: flex; flex-direction: column; gap: 8px;
  justify-content: center; align-items: center;
  color: #9cc6ea; font-size: 2rem;
}
.camera-placeholder p { font-size: 12px; margin: 0; }

.hidden-canvas { display: none; }

.message {
  padding: 10px 12px; border-radius: 10px; font-size: 12px; margin-bottom: 14px;
}
.message.error {
  border: 1px solid rgba(248, 113, 113, 0.3);
  background: rgba(127, 29, 29, 0.15);
  color: #fca5a5;
}

.actions { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; }
.actions:has(.primary:only-child) { grid-template-columns: 1fr; }

button {
  border: 0; border-radius: 11px; padding: 13px; cursor: pointer;
  font-weight: 700; font: inherit;
}
.primary { background: linear-gradient(135deg, #2f6fb8, #4997d0); color: white; }
.secondary { background: rgba(73, 151, 208, 0.12); color: #cddcee; }
button:disabled { opacity: 0.45; cursor: not-allowed; }

.footer-link { margin: 20px 0 0; text-align: center; font-size: 12px; }
.footer-link a { color: #6faad8; font-weight: 600; text-decoration: none; }
</style>
