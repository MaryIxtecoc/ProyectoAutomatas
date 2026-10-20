<script setup lang="ts">
import { ref } from 'vue'
import { apiFetch } from '~/services/api'

const email = ref('analista5@test.com')
const password = ref('Test1234!')
const resultado = ref('')
const error = ref('')

async function probarLogin() {
  resultado.value = ''
  error.value = ''

  try {
    const response = await apiFetch<{
      token: string
    }>('/Auth/login', {
      method: 'POST',
      body: JSON.stringify({
        email: email.value,
        password: password.value,
      }),
    })

    localStorage.setItem('token', response.token)

    resultado.value = 'Login correcto. JWT recibido.'
  } catch (err) {
    error.value = err instanceof Error
      ? err.message
      : 'Error desconocido.'
  }
}
</script>

<template>
  <main>
    <h1>Prueba de conexión</h1>

    <button @click="probarLogin">
      Probar login
    </button>

    <p v-if="resultado">
      {{ resultado }}
    </p>

    <p v-if="error">
      {{ error }}
    </p>
  </main>
</template>