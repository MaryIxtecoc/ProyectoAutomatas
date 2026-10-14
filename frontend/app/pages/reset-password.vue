<script setup lang="ts">
import { ref } from 'vue'
import { useRoute } from 'vue-router'
import { apiFetch } from '~/services/api'

const route = useRoute()

// El token es base64 (puede traer "+"); en una URL el "+" llega como espacio.
const queryToken = typeof route.query.token === 'string'
  ? route.query.token.replace(/ /g, '+')
  : ''

const token = ref(queryToken)
const newPassword = ref('')
const confirmPassword = ref('')

const loading = ref(false)
const error = ref('')
const success = ref('')

async function resetPassword() {
  error.value = ''
  success.value = ''

  if (!token.value.trim() || !newPassword.value || !confirmPassword.value) {
    error.value = 'Completa todos los campos.'
    return
  }

  if (newPassword.value.length < 8) {
    error.value = 'La contraseña debe tener al menos 8 caracteres.'
    return
  }

  if (newPassword.value !== confirmPassword.value) {
    error.value = 'Las contraseñas no coinciden.'
    return
  }

  loading.value = true

  try {
    const response = await apiFetch<{ message: string }>(
      '/Auth/reset-password',
      {
        method: 'POST',
        body: JSON.stringify({
          token: token.value.trim(),
          newPassword: newPassword.value,
        }),
      },
    )

    success.value = `${response.message} Redirigiendo al inicio de sesión...`

    setTimeout(() => navigateTo('/'), 2000)
  } catch (err) {
    error.value = err instanceof Error
      ? err.message
      : 'No fue posible restablecer la contraseña.'
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <main class="auth-page">
    <section class="panel">
      <div class="brand">
        <span class="brand-mark">L</span>
        <div>
          <h1>Léxica</h1>
          <p>Analiza. Descubre. Comprende.</p>
        </div>
      </div>

      <h2>Nueva contraseña</h2>
      <p class="subtitle">
        Ingresa el código de restablecimiento y elige tu nueva contraseña.
      </p>

      <form @submit.prevent="resetPassword">
        <div class="field">
          <label for="token">Código de restablecimiento</label>
          <input
            id="token"
            v-model="token"
            type="text"
            placeholder="Pega aquí tu código"
            autocomplete="off"
            :disabled="loading"
          >
        </div>

        <div class="field">
          <label for="newPassword">Nueva contraseña</label>
          <input
            id="newPassword"
            v-model="newPassword"
            type="password"
            placeholder="Mínimo 8 caracteres"
            autocomplete="new-password"
            :disabled="loading"
          >
        </div>

        <div class="field">
          <label for="confirmPassword">Confirmar contraseña</label>
          <input
            id="confirmPassword"
            v-model="confirmPassword"
            type="password"
            placeholder="Repite la contraseña"
            autocomplete="new-password"
            :disabled="loading"
          >
        </div>

        <div v-if="error" class="error" role="alert">
          {{ error }}
        </div>

        <div v-if="success" class="success" role="status">
          {{ success }}
        </div>

        <button type="submit" class="primary" :disabled="loading || !!success">
          {{ loading ? 'Guardando...' : 'Restablecer contraseña' }}
        </button>
      </form>

      <p class="footer-text">
        <NuxtLink to="/forgot-password">Solicitar un nuevo código</NuxtLink>
        ·
        <NuxtLink to="/">Iniciar sesión</NuxtLink>
      </p>
    </section>
  </main>
</template>

<style scoped>
.auth-page {
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 48px 24px;

  background-image:
    linear-gradient(180deg, rgba(6, 12, 28, 0.55) 0%, rgba(6, 12, 28, 0.75) 100%),
    url('/images/loginpage.png');
  background-size: cover;
  background-position: center;
  background-repeat: no-repeat;

  font-family: 'Poppins', 'Segoe UI', system-ui, sans-serif;
  color: #eaf1fb;
}

.panel {
  width: 100%;
  max-width: 420px;
  padding: 40px 36px;
  border-radius: 22px;
  background: rgba(11, 20, 41, 0.72);
  border: 1px solid rgba(73, 151, 208, 0.22);
  box-shadow: 0 30px 80px rgba(0, 0, 0, 0.5);
  backdrop-filter: blur(18px);
}

.brand { display: flex; align-items: center; gap: 14px; margin-bottom: 30px; }
.brand-mark {
  width: 46px; height: 46px; display: grid; place-items: center;
  border-radius: 14px;
  background: linear-gradient(135deg, rgba(73, 151, 208, 0.25), rgba(73, 151, 208, 0.05));
  border: 1px solid rgba(73, 151, 208, 0.4);
  color: #9cc6ea; font-weight: 700; font-size: 20px;
}
.brand h1 { margin: 0; font-size: 22px; font-weight: 700; }
.brand p { margin: 2px 0 0; font-size: 12px; color: #7f9dc4; }

h2 { margin: 0 0 6px; font-size: 24px; font-weight: 700; }
.subtitle { margin: 0 0 24px; color: #94aecf; font-size: 13.5px; line-height: 1.6; }

.field { margin-bottom: 18px; }
.field label {
  display: block; margin-bottom: 7px;
  font-size: 12.5px; color: #9cc6ea; font-weight: 600;
}
.field input {
  width: 100%; box-sizing: border-box; padding: 12px 14px;
  border: 1px solid rgba(73, 151, 208, 0.3);
  border-radius: 10px;
  background: rgba(6, 12, 28, 0.6);
  color: #eaf1fb; font-size: 14.5px; outline: none;
  transition: border-color 0.2s, box-shadow 0.2s;
}
.field input::placeholder { color: #4d6183; }
.field input:focus {
  border-color: #4997d0;
  box-shadow: 0 0 0 3px rgba(73, 151, 208, 0.15);
}

.error, .success {
  margin-bottom: 16px; padding: 10px 13px;
  border-radius: 10px; font-size: 13px;
}
.error {
  border: 1px solid rgba(248, 113, 113, 0.35);
  background: rgba(127, 29, 29, 0.2);
  color: #fca5a5;
}
.success {
  border: 1px solid rgba(74, 222, 128, 0.35);
  background: rgba(20, 83, 45, 0.25);
  color: #86efac;
}

button.primary {
  width: 100%; padding: 13px; border: 0; border-radius: 12px;
  background: linear-gradient(135deg, #2f6fb8, #4997d0);
  color: #fff; font-size: 15px; font-weight: 700; cursor: pointer;
  transition: transform 0.2s, box-shadow 0.2s, opacity 0.2s;
}
button.primary:hover:not(:disabled) {
  transform: translateY(-1px);
  box-shadow: 0 12px 30px rgba(73, 151, 208, 0.35);
}
button.primary:disabled { cursor: not-allowed; opacity: 0.6; }

.footer-text {
  margin: 26px 0 0; text-align: center;
  font-size: 13px; color: #7f9dc4;
}
.footer-text a { color: #4997d0; text-decoration: none; font-weight: 600; }
.footer-text a:hover { text-decoration: underline; }

@media (max-width: 640px) {
  .panel { padding: 32px 24px; }
}
</style>
