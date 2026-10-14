<script setup lang="ts">
import { PHONE_PATTERN, PHONE_ERROR, sanitizePhoneInput, isValidPhone } from '~/utils/phone'
import { computed, onMounted, reactive, ref } from 'vue'
import { apiFetch, logoutSession } from '~/services/api'

definePageMeta({ middleware: 'auth' })

interface Profile {
  id: string
  email: string
  phone: string
  birthDate: string
  nickname: string
  roleCode: string
  roleName: string
  notificationMethod: string
  photoOriginalPath?: string | null
  photoModifiedPath?: string | null
  createdAt: string
  credentialIssuedAt?: string | null
}

const notificationOptions = [
  { value: 'EMAIL', label: 'Correo electrónico' },
  { value: 'WHATSAPP', label: 'WhatsApp' },
  { value: 'BOTH', label: 'Ambos' },
]

const profile = ref<Profile | null>(null)
const loadingProfile = ref(true)
const loadError = ref('')

// ---- Datos personales ----
const form = reactive({
  nickname: '',
  phone: '',
  notificationMethod: 'EMAIL',
})
const savingProfile = ref(false)
const profileFb = reactive({ ok: '', err: '' })

// ---- Contraseña ----
const pass = reactive({ current: '', next: '', confirm: '' })
const savingPass = ref(false)
const passFb = reactive({ ok: '', err: '' })

// ---- Foto ----
const savedPhotoUrl = ref('')
const showPhotoEditor = ref(false)
const photoFb = reactive({ ok: '', err: '' })

async function onPhotoSaved(message: string) {
  showPhotoEditor.value = false
  photoFb.err = ''
  photoFb.ok = message
  await loadProfile()
}

// ---- Solicitud de corrección (email / fecha de nacimiento) ----
interface ChangeRequest {
  id: string
  requestedEmail: string | null
  requestedBirthDate: string | null
  reason: string
  status: 'PENDING' | 'APPROVED' | 'REJECTED'
  createdAt: string
  reviewedAt: string | null
}

const requestStatusLabels: Record<ChangeRequest['status'], string> = {
  PENDING: 'Pendiente',
  APPROVED: 'Aprobada',
  REJECTED: 'Rechazada',
}

const showCorrection = ref(false)
const myRequests = ref<ChangeRequest[]>([])
const correction = reactive({ email: '', birthDate: '', reason: '' })
const sendingCorrection = ref(false)
const correctionFb = reactive({ ok: '', err: '' })
const hasPendingRequest = computed(() => myRequests.value.some(r => r.status === 'PENDING'))

async function loadMyRequests() {
  try {
    myRequests.value = await apiFetch<ChangeRequest[]>('/ProfileChangeRequests/mine')
  } catch {
    myRequests.value = []
  }
}

async function sendCorrection() {
  correctionFb.ok = ''
  correctionFb.err = ''

  if (!correction.email.trim() && !correction.birthDate) {
    correctionFb.err = 'Indica el correo y/o la fecha de nacimiento correctos.'
    return
  }

  sendingCorrection.value = true

  try {
    const response = await apiFetch<{ message: string }>('/ProfileChangeRequests', {
      method: 'POST',
      // Solo estos tres campos: el estado, el revisor y el usuario los pone el backend.
      body: JSON.stringify({
        requestedEmail: correction.email.trim() || null,
        requestedBirthDate: correction.birthDate || null,
        reason: correction.reason,
      }),
    })

    correctionFb.ok = response.message
    Object.assign(correction, { email: '', birthDate: '', reason: '' })
    showCorrection.value = false
    await loadMyRequests()
  } catch (err) {
    correctionFb.err = err instanceof Error ? err.message : 'No fue posible enviar la solicitud.'
  } finally {
    sendingCorrection.value = false
  }
}

// ---- Credencial ----
const credentialBusy = ref(false)
const credentialFb = reactive({ ok: '', err: '' })

async function downloadCredential() {
  credentialFb.ok = ''
  credentialFb.err = ''
  credentialBusy.value = true

  try {
    const opened = await openAuthedFile('/Profile/credential')

    if (!opened) {
      credentialFb.err = 'No fue posible descargar la credencial. Si no tienes una, genérala primero.'
    }
  } finally {
    credentialBusy.value = false
  }
}

async function regenerateCredential() {
  credentialFb.ok = ''
  credentialFb.err = ''

  if (profile.value?.credentialIssuedAt &&
      !confirm('Se generará una credencial nueva y el QR de la anterior dejará de funcionar. ¿Continuar?')) {
    return
  }

  credentialBusy.value = true

  try {
    const response = await apiFetch<{ message: string }>('/Profile/credential/regenerate', {
      method: 'POST',
    })

    credentialFb.ok = response.message
    await loadProfile()
  } catch (err) {
    credentialFb.err = err instanceof Error ? err.message : 'No fue posible generar la credencial.'
  } finally {
    credentialBusy.value = false
  }
}

const initial = computed(() =>
  (profile.value?.nickname || '?').charAt(0).toUpperCase(),
)

async function loadProfile() {
  loadError.value = ''

  try {
    const data = await apiFetch<Profile>('/Profile/me')

    profile.value = data
    form.nickname = data.nickname
    form.phone = data.phone
    form.notificationMethod = data.notificationMethod

    if (data.photoModifiedPath) {
      savedPhotoUrl.value = (await fetchAuthedObjectUrl('/Profile/photo/modified')) ?? ''
    }
  } catch (err) {
    loadError.value = err instanceof Error
      ? err.message
      : 'No fue posible cargar tu perfil.'
  } finally {
    loadingProfile.value = false
  }
}

async function saveProfile() {
  profileFb.ok = ''
  profileFb.err = ''

  if (!form.nickname.trim() || !form.phone.trim()) {
    profileFb.err = 'Nickname y teléfono son obligatorios.'
    return
  }

  if (!isValidPhone(form.phone)) {
    profileFb.err = PHONE_ERROR
    return
  }

  savingProfile.value = true

  try {
    const response = await apiFetch<{ message: string }>('/Profile', {
      method: 'PUT',
      body: JSON.stringify({
        nickname: form.nickname.trim(),
        phone: form.phone.trim(),
        notificationMethod: form.notificationMethod,
      }),
    })

    profileFb.ok = response.message

    await loadProfile()
  } catch (err) {
    profileFb.err = err instanceof Error
      ? err.message
      : 'No fue posible actualizar el perfil.'
  } finally {
    savingProfile.value = false
  }
}

async function changePassword() {
  passFb.ok = ''
  passFb.err = ''

  if (!pass.current || !pass.next || !pass.confirm) {
    passFb.err = 'Completa todos los campos.'
    return
  }

  if (pass.next.length < 8) {
    passFb.err = 'La nueva contraseña debe tener al menos 8 caracteres.'
    return
  }

  if (pass.next !== pass.confirm) {
    passFb.err = 'Las contraseñas nuevas no coinciden.'
    return
  }

  if (pass.next === pass.current) {
    passFb.err = 'La nueva contraseña debe ser distinta a la actual.'
    return
  }

  savingPass.value = true

  try {
    const response = await apiFetch<{ message: string }>(
      '/Profile/password',
      {
        method: 'PATCH',
        body: JSON.stringify({
          currentPassword: pass.current,
          newPassword: pass.next,
        }),
      },
    )

    passFb.ok = response.message
    pass.current = ''
    pass.next = ''
    pass.confirm = ''
  } catch (err) {
    passFb.err = err instanceof Error
      ? err.message
      : 'No fue posible cambiar la contraseña.'
  } finally {
    savingPass.value = false
  }
}

function formatDate(value?: string) {
  if (!value) return '—'

  // "yyyy-MM-dd" → se arma la fecha local para evitar desfase de zona horaria.
  const [year, month, day] = value.slice(0, 10).split('-').map(Number)

  if (!year || !month || !day) return value

  return new Date(year, month - 1, day).toLocaleDateString('es-GT', {
    dateStyle: 'long',
  })
}

function logout() {
  logoutSession()
}

onMounted(() => {
  loadProfile()
  loadMyRequests()
})
</script>

<template>
  <main class="page">
    <header class="topbar">
      <div class="brand">
        <div class="brand-mark">L</div>

        <div>
          <h1>Léxica</h1>
          <p>Analiza. Descubre. Comprende.</p>
        </div>
      </div>

      <div class="topbar-actions">
        <NuxtLink to="/dashboard" class="ghost">← Dashboard</NuxtLink>

        <button class="ghost" @click="logout">
          Cerrar sesión
        </button>
      </div>
    </header>

    <section class="content">
      <div v-if="loadingProfile" class="state">
        Cargando perfil...
      </div>

      <div v-else-if="loadError" class="feedback err" role="alert">
        {{ loadError }}
      </div>

      <template v-else-if="profile">
        <div class="summary card">
          <div class="avatar">
            <img
              v-if="savedPhotoUrl"
              :src="savedPhotoUrl"
              alt="Foto de perfil"
            >
            <span v-else>{{ initial }}</span>
          </div>

          <div class="summary-info">
            <span class="eyebrow">MI PERFIL</span>
            <h2>{{ profile.nickname }}</h2>
            <p>{{ profile.email }}</p>

            <div class="badges">
              <span class="badge">{{ profile.roleName }}</span>
              <span v-if="profile.photoOriginalPath" class="badge ok">
                Foto registrada
              </span>
            </div>

            <div class="credential-actions">
              <button
                v-if="profile.credentialIssuedAt"
                type="button"
                class="credential-link"
                :disabled="credentialBusy"
                @click="downloadCredential"
              >
                Descargar mi credencial (PDF con QR)
              </button>
              <NuxtLink to="/credencial" class="credential-link"><AppIcon name="imagen" :size="18" /> Diseñar mi credencial</NuxtLink>
              <button
                type="button"
                class="credential-link"
                :disabled="credentialBusy"
                @click="regenerateCredential"
              >
                {{ credentialBusy ? 'Procesando...' : (profile.credentialIssuedAt ? 'Regenerar credencial' : 'Generar mi credencial') }}
              </button>
            </div>
            <p class="credential-status">
              {{ profile.credentialIssuedAt
                ? `Credencial activa emitida el ${new Date(profile.credentialIssuedAt).toLocaleDateString('es-GT')}`
                : 'Todavía no tienes una credencial activa.' }}
            </p>
            <div v-if="credentialFb.err" class="feedback err" role="alert">{{ credentialFb.err }}</div>
            <div v-if="credentialFb.ok" class="feedback ok" role="status">{{ credentialFb.ok }}</div>
          </div>
        </div>

        <div class="grid">
          <!-- Datos personales -->
          <form class="card" @submit.prevent="saveProfile">
            <h3>Datos personales</h3>

            <div class="field">
              <label>Correo electrónico</label>
              <input :value="profile.email" type="email" disabled>
            </div>

            <div class="field">
              <label>Fecha de nacimiento</label>
              <input :value="formatDate(profile.birthDate)" type="text" disabled>
            </div>

            <div class="correction">
              <button
                type="button"
                class="ghost block"
                :disabled="hasPendingRequest"
                @click="showCorrection = !showCorrection"
              >
                {{ hasPendingRequest ? 'Tienes una solicitud de corrección pendiente' : (showCorrection ? 'Cancelar corrección' : 'Corregir información (correo / fecha)') }}
              </button>

              <div v-if="showCorrection" class="correction-form">
                <p class="hint">
                  El cambio no se aplica de inmediato: un administrador revisará tu solicitud.
                  Llena solo lo que quieras corregir.
                </p>
                <div class="field">
                  <label for="corr-email">Correo correcto</label>
                  <input id="corr-email" v-model="correction.email" type="email" maxlength="254" placeholder="nuevo@correo.com">
                </div>
                <div class="field">
                  <label for="corr-birth">Fecha de nacimiento correcta</label>
                  <input id="corr-birth" v-model="correction.birthDate" type="date">
                </div>
                <div class="field">
                  <label for="corr-reason">Motivo (mínimo 10 caracteres)</label>
                  <textarea id="corr-reason" v-model="correction.reason" rows="3" maxlength="500" placeholder="Explica por qué necesitas la corrección" />
                </div>
                <button type="button" class="primary" :disabled="sendingCorrection" @click="sendCorrection">
                  {{ sendingCorrection ? 'Enviando...' : 'Enviar solicitud' }}
                </button>
              </div>

              <div v-if="correctionFb.err" class="feedback err" role="alert">{{ correctionFb.err }}</div>
              <div v-if="correctionFb.ok" class="feedback ok" role="status">{{ correctionFb.ok }}</div>

              <ul v-if="myRequests.length" class="requests">
                <li v-for="r in myRequests" :key="r.id">
                  <span class="req-status" :class="r.status.toLowerCase()">{{ requestStatusLabels[r.status] }}</span>
                  <span>
                    <template v-if="r.requestedEmail">Correo → {{ r.requestedEmail }}</template>
                    <template v-if="r.requestedEmail && r.requestedBirthDate"> · </template>
                    <template v-if="r.requestedBirthDate">Fecha → {{ formatDate(r.requestedBirthDate) }}</template>
                  </span>
                  <small>{{ new Date(r.createdAt).toLocaleDateString('es-GT') }}</small>
                </li>
              </ul>
            </div>

            <div class="field">
              <label for="nickname">Nickname</label>
              <input
                id="nickname"
                v-model="form.nickname"
                type="text"
                :disabled="savingProfile"
              >
            </div>

            <div class="field">
              <label for="phone">Teléfono</label>
              <input
                id="phone"
                v-model="form.phone"
                type="tel"
                inputmode="tel"
                maxlength="20"
                :pattern="PHONE_PATTERN"
                title="Solo números (se permite + al inicio, espacios o guiones)"
                @input="form.phone = sanitizePhoneInput(form.phone)"
                :disabled="savingProfile"
              >
            </div>

            <div class="field">
              <label for="notification">Notificaciones por</label>
              <select
                id="notification"
                v-model="form.notificationMethod"
                :disabled="savingProfile"
              >
                <option
                  v-for="option in notificationOptions"
                  :key="option.value"
                  :value="option.value"
                >
                  {{ option.label }}
                </option>
              </select>
            </div>

            <div v-if="profileFb.err" class="feedback err" role="alert">
              {{ profileFb.err }}
            </div>
            <div v-if="profileFb.ok" class="feedback ok" role="status">
              {{ profileFb.ok }}
            </div>

            <button type="submit" class="primary" :disabled="savingProfile">
              {{ savingProfile ? 'Guardando...' : 'Guardar cambios' }}
            </button>
          </form>

          <div class="column">
            <!-- Foto -->
            <div class="card">
              <h3>Fotografía</h3>

              <p class="hint">
                Toma o sube una foto, encuádrala y decórala con filtros y stickers.
                Tu inicio de sesión facial no cambia: se gestiona en "Acceso facial".
              </p>

              <div v-if="photoFb.err" class="feedback err" role="alert">
                {{ photoFb.err }}
              </div>
              <div v-if="photoFb.ok" class="feedback ok" role="status">
                {{ photoFb.ok }}
                <template v-if="profile.credentialIssuedAt">
                  Tu credencial aún muestra la foto anterior.
                  <button type="button" class="inline-action" :disabled="credentialBusy" @click="regenerateCredential">
                    Regenerar credencial
                  </button>
                </template>
              </div>

              <button type="button" class="primary" @click="showPhotoEditor = true">
                <AppIcon name="camara" /> Cambiar foto
              </button>

              <PhotoEditor v-if="showPhotoEditor" @saved="onPhotoSaved" @close="showPhotoEditor = false" />
            </div>

            <!-- Contraseña -->
            <form class="card" @submit.prevent="changePassword">
              <h3>Cambiar contraseña</h3>

              <div class="field">
                <label for="currentPassword">Contraseña actual</label>
                <input
                  id="currentPassword"
                  v-model="pass.current"
                  type="password"
                  autocomplete="current-password"
                  :disabled="savingPass"
                >
              </div>

              <div class="field">
                <label for="newPassword">Nueva contraseña</label>
                <input
                  id="newPassword"
                  v-model="pass.next"
                  type="password"
                  placeholder="Mínimo 8 caracteres"
                  autocomplete="new-password"
                  :disabled="savingPass"
                >
              </div>

              <div class="field">
                <label for="confirmPassword">Confirmar nueva contraseña</label>
                <input
                  id="confirmPassword"
                  v-model="pass.confirm"
                  type="password"
                  autocomplete="new-password"
                  :disabled="savingPass"
                >
              </div>

              <div v-if="passFb.err" class="feedback err" role="alert">
                {{ passFb.err }}
              </div>
              <div v-if="passFb.ok" class="feedback ok" role="status">
                {{ passFb.ok }}
              </div>

              <button type="submit" class="primary" :disabled="savingPass">
                {{ savingPass ? 'Actualizando...' : 'Actualizar contraseña' }}
              </button>
            </form>

            <!-- Biometría -->
            <div class="card">
              <h3>Acceso facial</h3>

              <p class="hint">
                Registra o elimina tu rostro para iniciar sesión con biometría.
              </p>

              <NuxtLink to="/biometrics?from=/perfil" class="ghost block">
                Configurar reconocimiento facial
              </NuxtLink>
            </div>
          </div>
        </div>
      </template>
    </section>
  </main>
</template>

<style scoped>
.page {
  min-height: 100vh;
  background:
    linear-gradient(180deg, rgba(6, 12, 28, 0.55) 0%, rgba(6, 12, 28, 0.78) 100%),
    url('/images/apartado2.png') center / cover no-repeat fixed,
    #07101f;
  color: #eaf1fb;
  font-family: 'Poppins', 'Segoe UI', system-ui, sans-serif;
}

.topbar {
  height: 76px;
  padding: 0 5vw;
  display: flex;
  align-items: center;
  justify-content: space-between;
  border-bottom: 1px solid rgba(73, 151, 208, 0.12);
  background: rgba(5, 13, 27, 0.75);
  backdrop-filter: blur(18px);
}

.brand { display: flex; align-items: center; gap: 12px; }
.brand-mark {
  width: 40px; height: 40px; display: grid; place-items: center;
  border-radius: 12px;
  background: linear-gradient(135deg, rgba(73, 151, 208, 0.3), rgba(73, 151, 208, 0.05));
  border: 1px solid rgba(73, 151, 208, 0.35);
  color: #9cc6ea; font-weight: 800;
}
.brand h1 { margin: 0; font-size: 19px; }
.brand p { margin: 1px 0 0; font-size: 10px; color: #6682a7; }

.topbar-actions { display: flex; gap: 10px; }

.ghost {
  padding: 9px 16px;
  border: 1px solid rgba(73, 151, 208, 0.25);
  border-radius: 9px;
  background: rgba(73, 151, 208, 0.06);
  color: #a9c5e3;
  font-size: 13.5px;
  text-decoration: none;
  cursor: pointer;
  transition: background 0.2s;
}
.ghost:hover { background: rgba(73, 151, 208, 0.14); }
.ghost.block { display: block; text-align: center; }

.content {
  width: min(960px, 90%);
  margin: 0 auto;
  padding: 48px 0 90px;
}

.state { padding: 60px 0; text-align: center; color: #7f9dc4; }

.card {
  padding: 26px;
  border: 1px solid rgba(73, 151, 208, 0.16);
  border-radius: 18px;
  background: rgba(11, 20, 41, 0.7);
}
.card h3 { margin: 0 0 18px; font-size: 16px; }

.summary {
  display: flex;
  align-items: center;
  gap: 22px;
  margin-bottom: 22px;
}

.avatar {
  width: 88px; height: 88px; flex-shrink: 0;
  display: grid; place-items: center; overflow: hidden;
  border-radius: 50%;
  border: 2px solid rgba(73, 151, 208, 0.4);
  background: linear-gradient(135deg, rgba(73, 151, 208, 0.3), rgba(73, 151, 208, 0.05));
  color: #9cc6ea; font-size: 34px; font-weight: 700;
}
.avatar img { width: 100%; height: 100%; object-fit: cover; }

.eyebrow { font-size: 10.5px; letter-spacing: 1.4px; color: #4997d0; font-weight: 700; }
.summary-info h2 { margin: 4px 0 2px; font-size: 24px; }
.summary-info p { margin: 0; color: #94aecf; font-size: 13.5px; }

.badges { display: flex; gap: 8px; margin-top: 12px; flex-wrap: wrap; }
.badge {
  padding: 4px 12px; border-radius: 999px;
  border: 1px solid rgba(73, 151, 208, 0.35);
  background: rgba(73, 151, 208, 0.1);
  color: #9cc6ea; font-size: 11.5px; font-weight: 600;
}
.badge.ok {
  border-color: rgba(74, 222, 128, 0.35);
  background: rgba(20, 83, 45, 0.25);
  color: #86efac;
}

.inline-action {
  margin-left: 6px; padding: 0; border: 0; background: none; color: #9cc6ea;
  font: inherit; font-weight: 700; text-decoration: underline; cursor: pointer;
}

.credential-actions { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 12px; }
.credential-status { margin: 8px 0 0; font-size: 11.5px; color: #6682a7; }
.credential-link:disabled { opacity: 0.5; cursor: not-allowed; }

a.credential-link { text-decoration: none; display: inline-block; }
.credential-link {
  padding: 8px 14px;
  border-radius: 10px;
  border: 1px solid rgba(73, 151, 208, 0.35);
  background: rgba(73, 151, 208, 0.08);
  color: #9cc6ea;
  font-size: 12px;
  font-weight: 600;
  cursor: pointer;
}

.grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 22px;
  align-items: start;
}
.column { display: flex; flex-direction: column; gap: 22px; }

.field { margin-bottom: 16px; }
.field textarea {
  width: 100%; padding: 12px; border-radius: 10px; border: 1px solid rgba(73, 151, 208, 0.25);
  background: #0a162a; color: #eaf1fb; font: inherit; resize: vertical; box-sizing: border-box;
}
.correction { margin-bottom: 18px; }
.correction .ghost.block { width: 100%; }
.correction-form { margin-top: 12px; padding: 14px; border-radius: 12px; background: rgba(5, 11, 22, 0.5); border: 1px solid rgba(73, 151, 208, 0.15); }
.requests { list-style: none; margin: 12px 0 0; padding: 0; display: flex; flex-direction: column; gap: 6px; font-size: 12px; }
.requests li { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; color: #cddcee; }
.requests small { color: #6682a7; margin-left: auto; }
.req-status { padding: 2px 8px; border-radius: 999px; font-size: 10.5px; font-weight: 700; background: rgba(250, 204, 21, 0.12); color: #fde68a; }
.req-status.approved { background: rgba(34, 197, 94, 0.15); color: #86efac; }
.req-status.rejected { background: rgba(248, 113, 113, 0.15); color: #fca5a5; }
.field label {
  display: block; margin-bottom: 6px;
  font-size: 12px; color: #9cc6ea; font-weight: 600;
}
.field input, .field select {
  width: 100%; box-sizing: border-box; padding: 11px 13px;
  border: 1px solid rgba(73, 151, 208, 0.3);
  border-radius: 10px;
  background: rgba(6, 12, 28, 0.6);
  color: #eaf1fb; font-size: 14px; outline: none;
  transition: border-color 0.2s, box-shadow 0.2s;
}
.field input:focus, .field select:focus {
  border-color: #4997d0;
  box-shadow: 0 0 0 3px rgba(73, 151, 208, 0.15);
}
.field input:disabled { opacity: 0.55; cursor: not-allowed; }

.hint { margin: -8px 0 14px; color: #7f9dc4; font-size: 12.5px; line-height: 1.5; }

.file-input {
  width: 100%; box-sizing: border-box; margin-bottom: 14px;
  padding: 10px; border-radius: 10px;
  border: 1px dashed rgba(73, 151, 208, 0.35);
  background: rgba(6, 12, 28, 0.5);
  color: #a9c5e3; font-size: 13px;
}

.feedback {
  margin-bottom: 14px; padding: 10px 13px;
  border-radius: 10px; font-size: 13px;
}
.feedback.err {
  border: 1px solid rgba(248, 113, 113, 0.35);
  background: rgba(127, 29, 29, 0.2);
  color: #fca5a5;
}
.feedback.ok {
  border: 1px solid rgba(74, 222, 128, 0.35);
  background: rgba(20, 83, 45, 0.25);
  color: #86efac;
}

button.primary {
  width: 100%; padding: 12px; border: 0; border-radius: 12px;
  background: linear-gradient(135deg, #2f6fb8, #4997d0);
  color: #fff; font-size: 14.5px; font-weight: 700; cursor: pointer;
  transition: transform 0.2s, box-shadow 0.2s, opacity 0.2s;
}
button.primary:hover:not(:disabled) {
  transform: translateY(-1px);
  box-shadow: 0 12px 30px rgba(73, 151, 208, 0.35);
}
button.primary:disabled { cursor: not-allowed; opacity: 0.5; }

@media (max-width: 800px) {
  .grid { grid-template-columns: 1fr; }
  .summary { flex-direction: column; text-align: center; }
  .badges { justify-content: center; }
}
</style>
