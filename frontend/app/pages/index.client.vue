<script setup lang="ts">
import { ref, onMounted, nextTick } from 'vue'
import { apiFetch } from '~/services/api'
import { useRecaptcha } from 'vue3-recaptcha-v2'

const email = ref('')
const password = ref('')
const remember = ref(false)
const loading = ref(false)
const error = ref('')

const recaptchaEl = ref<HTMLElement | null>(null)

const recaptchaToken = ref('')
let renderRecaptcha = (_el?: HTMLElement | null) => {}
let resetRecaptcha = () => {}

onMounted(async () => {
  const recaptcha = useRecaptcha()
  if (recaptcha) {
    recaptchaToken.value = recaptcha.token
    renderRecaptcha = recaptcha.render
    resetRecaptcha = recaptcha.reset
  }

  await nextTick()
  if (recaptchaEl.value && renderRecaptcha) {
    renderRecaptcha(recaptchaEl.value)
  }
})

async function login() {
  error.value = ''

  if (!email.value || !password.value) {
    error.value = 'Ingresa tu correo y contraseña.'
    return
  }

  if (!recaptchaToken.value) {
    error.value = 'Completa la verificación reCAPTCHA.'
    return
  }

  loading.value = true

  try {
    const response = await apiFetch<{
      token: string
    }>('/Auth/login', {
      method: 'POST',
      body: JSON.stringify({
        email: email.value,
        password: password.value,
        recaptchaToken: recaptchaToken.value,
      }),
    })

    localStorage.setItem('token', response.token)

    await navigateTo('/dashboard')
  } catch (err) {
    error.value = err instanceof Error
      ? err.message
      : 'No fue posible iniciar sesión.'
    resetRecaptcha()
  } finally {
    loading.value = false
  }
}

function loginWithFace() {
  navigateTo('/auth/facial')
}

function loginWithQr() {
  navigateTo('/auth/qr')
}
</script>

<template>
<ClientOnly>
  <main class="login-page">
    <section class="panel">
      <div class="brand">
        <span class="brand-mark">L</span>
        <div>
          <h1>Léxica</h1>
          <p>Analiza. Descubre. Comprende.</p>
        </div>
      </div>

      <div class="welcome">
        <h2>¡Bienvenido!</h2>
        <p>Inicia sesión para continuar con tu análisis léxico.</p>
      </div>

      <form @submit.prevent="login">
        <div class="field">
          <label for="email">Correo o usuario</label>
          <input
            id="email"
            v-model="email"
            type="email"
            placeholder="correo@ejemplo.com"
            autocomplete="email"
            :disabled="loading"
          >
        </div>

        <div class="field">
          <label for="password">Contraseña</label>
          <input
            id="password"
            v-model="password"
            type="password"
            placeholder="••••••••"
            autocomplete="current-password"
            :disabled="loading"
          >
        </div>

        <div class="row">
          <label class="remember">
            <input v-model="remember" type="checkbox">
            Recordarme
          </label>
          <NuxtLink to="/forgot-password" class="link">¿Olvidaste tu contraseña?</NuxtLink>
        </div>

        <ClientOnly>
        <div class="recaptcha-wrap">
          <div ref="recaptchaEl"></div>
        </div>
      </ClientOnly>

        <div v-if="error" class="error" role="alert">
          {{ error }}
        </div>

        <button type="submit" class="primary" :disabled="loading">
          {{ loading ? 'Verificando...' : 'Iniciar sesión' }}
        </button>

        <div class="divider"><span>o</span></div>

        <div class="alt-methods">
          <button type="button" class="alt" @click="loginWithFace">
            Con tu rostro
          </button>
          <button type="button" class="alt" @click="loginWithQr">
            Con código QR
          </button>
        </div>
      </form>

      <p class="footer-text">
        ¿No tienes una cuenta? <NuxtLink to="/register">Regístrate aquí</NuxtLink>
      </p>
    </section>
  </main>
  </ClientOnly>
</template>

<style scoped>
.login-page {
  position: relative;
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
  padding: 48px 24px;

  background-image:
    linear-gradient(
      180deg,
      rgba(6, 12, 28, 0.55) 0%,
      rgba(6, 12, 28, 0.75) 100%
    ),
    url('/images/loginpage.png');

  background-size: cover;
  background-position: center;
  background-repeat: no-repeat;

  font-family: 'Poppins', 'Segoe UI', system-ui, sans-serif;
  color: #eaf1fb;
}

.panel {
  position: relative;
  z-index: 1;
  width: 100%;
  max-width: 420px;
  padding: 40px 36px;
  border-radius: 22px;
  background: rgba(11, 20, 41, 0.72);
  border: 1px solid rgba(73, 151, 208, 0.22);
  box-shadow: 0 30px 80px rgba(0, 0, 0, 0.5);
  backdrop-filter: blur(18px);
  animation: rise 0.6s ease both;
}

@keyframes rise {
  from { opacity: 0; transform: translateY(14px); }
  to   { opacity: 1; transform: translateY(0); }
}

.brand {
  display: flex;
  align-items: center;
  gap: 14px;
  margin-bottom: 30px;
}

.brand-mark {
  width: 46px;
  height: 46px;
  display: grid;
  place-items: center;
  border-radius: 14px;
  background: linear-gradient(135deg, rgba(73, 151, 208, 0.25), rgba(73, 151, 208, 0.05));
  border: 1px solid rgba(73, 151, 208, 0.4);
  color: #9cc6ea;
  font-weight: 700;
  font-size: 20px;
}

.brand h1 {
  margin: 0;
  font-size: 22px;
  font-weight: 700;
  letter-spacing: 0.3px;
}
.brand p {
  margin: 2px 0 0;
  font-size: 12px;
  color: #7f9dc4;
}

.welcome { margin-bottom: 26px; }
.welcome h2 { margin: 0 0 6px; font-size: 26px; font-weight: 700; }
.welcome p { margin: 0; color: #94aecf; font-size: 13.5px; line-height: 1.6; }

.field { margin-bottom: 18px; }
.field label {
  display: block;
  margin-bottom: 7px;
  font-size: 12.5px;
  color: #9cc6ea;
  font-weight: 600;
}
.field input {
  width: 100%;
  box-sizing: border-box;
  padding: 12px 14px;
  border: 1px solid rgba(73, 151, 208, 0.3);
  border-radius: 10px;
  background: rgba(6, 12, 28, 0.6);
  color: #eaf1fb;
  font-size: 14.5px;
  outline: none;
  transition: border-color 0.2s, box-shadow 0.2s;
}
.field input::placeholder { color: #4d6183; }
.field input:focus {
  border-color: #4997d0;
  box-shadow: 0 0 0 3px rgba(73, 151, 208, 0.15);
}

.row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 20px;
  font-size: 12.5px;
}
.remember { display: flex; align-items: center; gap: 6px; color: #9cc6ea; }
.link { color: #4997d0; text-decoration: none; }
.link:hover { text-decoration: underline; }

.recaptcha-wrap {
  display: flex;
  justify-content: center;
  margin-bottom: 16px;
  transform: scale(0.95);
  transform-origin: center;
}

.error {
  margin-bottom: 16px;
  padding: 10px 13px;
  border: 1px solid rgba(248, 113, 113, 0.35);
  border-radius: 10px;
  background: rgba(127, 29, 29, 0.2);
  color: #fca5a5;
  font-size: 13px;
}

button.primary {
  width: 100%;
  padding: 13px;
  border: 0;
  border-radius: 12px;
  background: linear-gradient(135deg, #2f6fb8, #4997d0);
  color: #fff;
  font-size: 15px;
  font-weight: 700;
  cursor: pointer;
  transition: transform 0.2s, box-shadow 0.2s, opacity 0.2s;
}
button.primary:hover:not(:disabled) {
  transform: translateY(-1px);
  box-shadow: 0 12px 30px rgba(73, 151, 208, 0.35);
}
button.primary:disabled { cursor: not-allowed; opacity: 0.6; }

.divider {
  display: flex;
  align-items: center;
  gap: 12px;
  margin: 20px 0;
  color: #4d6183;
  font-size: 12px;
}
.divider::before, .divider::after {
  content: '';
  flex: 1;
  height: 1px;
  background: rgba(73, 151, 208, 0.2);
}

.alt-methods {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}
.alt {
  padding: 11px;
  border-radius: 10px;
  border: 1px solid rgba(73, 151, 208, 0.3);
  background: rgba(73, 151, 208, 0.06);
  color: #cfe1f5;
  font-size: 13px;
  font-weight: 600;
  cursor: pointer;
  transition: background 0.2s, transform 0.2s;
}
.alt:hover { background: rgba(73, 151, 208, 0.14); transform: translateY(-1px); }

.footer-text {
  margin: 26px 0 0;
  text-align: center;
  font-size: 13px;
  color: #7f9dc4;
}
.footer-text a { color: #4997d0; text-decoration: none; font-weight: 600; }
.footer-text a:hover { text-decoration: underline; }

@media (max-width: 640px) {
  .panel { padding: 32px 24px; }
}

@media (prefers-reduced-motion: reduce) {
  .panel { animation: none; }
}
</style>