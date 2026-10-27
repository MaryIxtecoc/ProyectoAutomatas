<script setup lang="ts">
import { PHONE_PATTERN, PHONE_ERROR, sanitizePhoneInput, isValidPhone } from '~/utils/phone'
import { computed, onMounted, ref } from 'vue'
import { apiFetch } from '~/services/api'

definePageMeta({ middleware: 'staff' })

interface User {
  id: string
  email: string
  phone: string
  birthDate: string
  nickname: string
  roleId: number
  roleCode?: string
  notificationMethod: string
  isActive: boolean
  createdAt: string
  lastLoginAt?: string | null
  analysisCount?: number
}

interface UserForm {
  email: string
  phone: string
  birthDate: string
  nickname: string
  password: string
  roleId: number
  notificationMethod: string
}

// Se calcula al montar: en el render del servidor no hay localStorage.
const isAdmin = ref(false)

const users = ref<User[]>([])
const loading = ref(true)
const saving = ref(false)
const error = ref('')
const success = ref('')

const showModal = ref(false)
const editingUser = ref<User | null>(null)

const search = ref('')

const form = ref<UserForm>({
  email: '',
  phone: '',
  birthDate: '',
  nickname: '',
  password: '',
  roleId: 3,
  notificationMethod: 'EMAIL',
})

// El rol ANALISTA (3) es el único que puede asignarse desde este panel al
// crear un usuario nuevo por fuera del autorregistro; ADMIN y SUPERVISOR
// se asignan aparte, con más cuidado (ver ChangeRole).
const roles = [
  { id: 1, name: 'Administrador', code: 'ADMIN' },
  { id: 2, name: 'Supervisor', code: 'SUPERVISOR' },
  { id: 3, name: 'Analista', code: 'ANALISTA' },
]

const filteredUsers = computed(() => {
  const value = search.value.trim().toLowerCase()

  if (!value) return users.value

  return users.value.filter(user =>
    user.nickname?.toLowerCase().includes(value) ||
    user.email?.toLowerCase().includes(value) ||
    user.phone?.toLowerCase().includes(value)
  )
})

function roleName(roleId: number) {
  return roles.find(role => role.id === roleId)?.name ?? 'Desconocido'
}

function roleCode(roleId: number) {
  return roles.find(role => role.id === roleId)?.code ?? ''
}

function resetForm() {
  form.value = {
    email: '',
    phone: '',
    birthDate: '',
    nickname: '',
    password: '',
    roleId: 3,
    notificationMethod: 'EMAIL',
  }
}

function openCreate() {
  editingUser.value = null
  resetForm()
  error.value = ''
  success.value = ''
  showModal.value = true
}

function openEdit(user: User) {
  editingUser.value = user

  form.value = {
    email: user.email,
    phone: user.phone,
    birthDate: user.birthDate?.slice(0, 10) ?? '',
    nickname: user.nickname,
    password: '',
    roleId: user.roleId,
    notificationMethod: user.notificationMethod,
  }

  error.value = ''
  success.value = ''
  showModal.value = true
}

function closeModal() {
  if (saving.value) return

  showModal.value = false
  editingUser.value = null
  resetForm()
}

async function loadUsers() {
  loading.value = true
  error.value = ''

  try {
    users.value = await apiFetch<User[]>('/Users')
  } catch (err: any) {
    error.value = err?.message ?? 'No se pudieron cargar los usuarios.'
  } finally {
    loading.value = false
  }
}

async function saveUser() {
  error.value = ''

  if (!isValidPhone(form.value.phone)) {
    error.value = PHONE_ERROR
    return
  }
  success.value = ''
  saving.value = true

  try {
    if (editingUser.value) {
      await apiFetch(`/Users/${editingUser.value.id}`, {
        method: 'PUT',
        body: JSON.stringify({
          email: form.value.email,
          phone: form.value.phone,
          birthDate: form.value.birthDate,
          nickname: form.value.nickname,
          notificationMethod: form.value.notificationMethod,
        }),
      })

      // El endpoint recibe el número del rol en el cuerpo (no un objeto).
      // Solo se llama si cambió: cambiar el rol cierra las sesiones del usuario.
      if (form.value.roleId !== editingUser.value.roleId) {
        await apiFetch(`/Users/${editingUser.value.id}/role`, {
          method: 'PATCH',
          body: JSON.stringify(form.value.roleId),
        })
      }

      success.value = 'Usuario actualizado correctamente.'
    } else {
      await apiFetch('/Users', {
        method: 'POST',
        body: JSON.stringify(form.value),
      })

      success.value = 'Usuario creado correctamente.'
    }

    await loadUsers()
    showModal.value = false
    editingUser.value = null
    resetForm()
  } catch (err: any) {
    error.value = err?.message ?? 'No se pudo guardar el usuario.'
  } finally {
    saving.value = false
  }
}

async function toggleStatus(user: User) {
  error.value = ''
  success.value = ''

  const action = user.isActive ? 'desactivar' : 'activar'

  if (!confirm(`¿Deseas ${action} a ${user.nickname}?`)) {
    return
  }

  try {
    await apiFetch(`/Users/${user.id}/status`, {
      method: 'PATCH',
      body: JSON.stringify({
        isActive: !user.isActive,
      }),
    })

    success.value = `Usuario ${user.isActive ? 'desactivado' : 'activado'} correctamente.`
    await loadUsers()
  } catch (err: any) {
    error.value = err?.message ?? 'No se pudo cambiar el estado del usuario.'
  }
}

async function deleteUser(user: User) {
  error.value = ''
  success.value = ''

  if (
    !confirm(
      `¿Deseas eliminar a ${user.nickname}? Esta acción realizará la eliminación lógica del usuario.`
    )
  ) {
    return
  }

  try {
    await apiFetch(`/Users/${user.id}`, {
      method: 'DELETE',
    })

    success.value = 'Usuario eliminado correctamente.'
    await loadUsers()
  } catch (err: any) {
    error.value = err?.message ?? 'No se pudo eliminar el usuario.'
  }
}

onMounted(() => {
  isAdmin.value = getAuthRole() === 'ADMIN'
  loadUsers()
})
</script>

<template>
  <div class="page">
    <header class="topbar">
      <div class="brand">
        <span class="brand-mark">L</span>
        <div>
          <h1>Léxica</h1>
          <p>Gestión de usuarios</p>
        </div>
      </div>

      <nav class="topbar-actions">
        <NuxtLink to="/dashboard" class="link-button">← Análisis</NuxtLink>
        <NuxtLink to="/supervisor" class="link-button">Supervisión</NuxtLink>
        <NuxtLink v-if="isAdmin" to="/auditoria" class="link-button">Auditoría</NuxtLink>
        <NuxtLink v-if="isAdmin" to="/solicitudes" class="link-button">Solicitudes</NuxtLink>
        <NuxtLink v-if="isAdmin" to="/parametros" class="link-button">Parámetros</NuxtLink>
        <NuxtLink to="/perfil" class="link-button">Mi perfil</NuxtLink>
      </nav>
    </header>

    <section class="title-row">
      <div>
        <h2>Usuarios</h2>
        <p>Administra usuarios, roles y estados de acceso.</p>
      </div>

      <button v-if="isAdmin" class="primary-button" @click="openCreate">
        + Nuevo usuario
      </button>
    </section>

    <div v-if="error" class="alert error">
      {{ error }}
    </div>

    <div v-if="success" class="alert success">
      {{ success }}
    </div>

    <section class="toolbar">
      <input
        v-model="search"
        type="search"
        placeholder="Buscar por nombre, correo o teléfono..."
      />

      <span class="count">
        {{ filteredUsers.length }} usuario{{ filteredUsers.length === 1 ? '' : 's' }}
      </span>
    </section>

    <section class="card">
      <div v-if="loading" class="empty">
        Cargando usuarios...
      </div>

      <div v-else-if="filteredUsers.length === 0" class="empty">
        No se encontraron usuarios.
      </div>

      <div v-else class="table-wrapper">
        <table>
          <thead>
            <tr>
              <th>Usuario</th>
              <th>Contacto</th>
              <th>Rol</th>
              <th>Estado</th>
              <th>Último acceso</th>
              <th>Análisis</th>
              <th>Creado</th>
              <th v-if="isAdmin">Acciones</th>
            </tr>
          </thead>

          <tbody>
            <tr v-for="user in filteredUsers" :key="user.id">
              <td>
                <div class="user-cell">
                  <div class="avatar">
                    {{ user.nickname?.charAt(0)?.toUpperCase() ?? '?' }}
                  </div>

                  <div>
                    <strong>{{ user.nickname }}</strong>
                    <small>{{ user.email }}</small>
                  </div>
                </div>
              </td>

              <td>
                <span>{{ user.phone }}</span>
              </td>

              <td>
                <span class="role">
                  {{ roleName(user.roleId) }}
                </span>
              </td>

              <td>
                <span
                  class="status"
                  :class="user.isActive ? 'active' : 'inactive'"
                >
                  {{ user.isActive ? 'Activo' : 'Inactivo' }}
                </span>
              </td>

              <td>
                {{ user.lastLoginAt ? new Date(user.lastLoginAt).toLocaleString('es-GT') : 'Nunca' }}
              </td>

              <td>
                {{ user.analysisCount ?? 0 }}
              </td>

              <td>
                {{ new Date(user.createdAt).toLocaleDateString('es-GT') }}
              </td>

              <td v-if="isAdmin">
                <div class="actions">
                  <button
                    class="action-button"
                    title="Editar"
                    @click="openEdit(user)"
                  >
                    Editar
                  </button>

                  <button
                    class="action-button"
                    :class="user.isActive ? 'danger' : 'activate'"
                    @click="toggleStatus(user)"
                  >
                    {{ user.isActive ? 'Desactivar' : 'Activar' }}
                  </button>

                  <button
                    class="action-button danger"
                    @click="deleteUser(user)"
                  >
                    Eliminar
                  </button>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>

    <!-- Modal -->
    <div v-if="showModal" class="modal-backdrop" @click.self="closeModal">
      <section class="modal">
        <div class="modal-header">
          <div>
            <h2>
              {{ editingUser ? 'Editar usuario' : 'Nuevo usuario' }}
            </h2>

            <p>
              {{
                editingUser
                  ? 'Actualiza la información del usuario.'
                  : 'Crea una nueva cuenta de acceso.'
              }}
            </p>
          </div>

          <button class="close-button" @click="closeModal">
            ×
          </button>
        </div>

        <form @submit.prevent="saveUser">
          <div class="form-grid">
            <label>
              Correo electrónico
              <input
                v-model="form.email"
                type="email"
                required
                maxlength="254"
                placeholder="usuario@correo.com"
              />
            </label>

            <label>
              Teléfono
              <input
                v-model="form.phone"
                type="tel"
                inputmode="tel"
                maxlength="20"
                :pattern="PHONE_PATTERN"
                title="Solo números (se permite + al inicio, espacios o guiones)"
                @input="form.phone = sanitizePhoneInput(form.phone)"
                required
                placeholder="5555-5555"
              />
            </label>

            <label>
              Nombre / nickname
              <input
                v-model="form.nickname"
                type="text"
                required
                minlength="3"
                maxlength="30"
                placeholder="Nombre del usuario"
              />
            </label>

            <label>
              Fecha de nacimiento
              <input
                v-model="form.birthDate"
                type="date"
                required
              />
            </label>

            <label>
              Rol
              <select v-model.number="form.roleId">
                <option
                  v-for="role in roles"
                  :key="role.id"
                  :value="role.id"
                >
                  {{ role.name }}
                </option>
              </select>
            </label>

            <label>
              Método de notificación
              <select v-model="form.notificationMethod">
                <option value="EMAIL">Correo electrónico</option>
                <option value="WHATSAPP">WhatsApp</option>
                <option value="BOTH">Ambos</option>
              </select>
            </label>

            <label v-if="!editingUser" class="full">
              Contraseña
              <input
                v-model="form.password"
                type="password"
                required
                minlength="8"
                placeholder="Mínimo 8 caracteres"
              />
            </label>
          </div>

          <div class="modal-actions">
            <button
              type="button"
              class="secondary-button"
              :disabled="saving"
              @click="closeModal"
            >
              Cancelar
            </button>

            <button
              type="submit"
              class="primary-button"
              :disabled="saving"
            >
              {{ saving ? 'Guardando...' : 'Guardar usuario' }}
            </button>
          </div>
        </form>
      </section>
    </div>
  </div>
</template>

<style scoped>
.page {
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

.title-row { display: flex; align-items: center; justify-content: space-between; gap: 16px; flex-wrap: wrap; margin-bottom: 16px; }
.title-row h2 { margin: 0 0 4px; font-size: 20px; }
.title-row p { margin: 0; color: #819ab9; font-size: 13px; }

button { font: inherit; cursor: pointer; }
button:disabled { opacity: 0.45; cursor: not-allowed; }
.primary-button {
  border: 0; border-radius: 10px; padding: 10px 16px; font-size: 13px; font-weight: 700;
  background: linear-gradient(135deg, #2f6fb8, #4997d0); color: white;
}
.secondary-button {
  border: 1px solid rgba(73, 151, 208, 0.25); border-radius: 10px; padding: 10px 16px; font-size: 13px; font-weight: 600;
  background: rgba(73, 151, 208, 0.08); color: #cddcee;
}

.alert { padding: 10px 14px; border-radius: 10px; font-size: 12.5px; margin-bottom: 14px; }
.alert.error { background: rgba(127, 29, 29, 0.15); color: #fca5a5; border: 1px solid rgba(248, 113, 113, 0.3); }
.alert.success { background: rgba(34, 197, 94, 0.12); color: #86efac; border: 1px solid rgba(34, 197, 94, 0.25); }

.toolbar { display: flex; align-items: center; justify-content: space-between; gap: 12px; margin-bottom: 14px; flex-wrap: wrap; }
.toolbar input {
  width: min(360px, 100%); padding: 10px 12px; border-radius: 10px; border: 1px solid rgba(73, 151, 208, 0.25);
  background: #0a162a; color: #eaf1fb; font: inherit; font-size: 13px; outline: none;
}
.toolbar input:focus { border-color: #4997d0; }
.count { color: #86a5c8; font-size: 12.5px; }

.card { border-radius: 14px; background: rgba(10, 21, 40, 0.75); border: 1px solid rgba(73, 151, 208, 0.18); overflow: hidden; }
.empty { padding: 40px; text-align: center; color: #86a5c8; }
.table-wrapper { overflow-x: auto; }
table { width: 100%; border-collapse: collapse; font-size: 12.5px; }
th { text-align: left; padding: 12px; color: #86a5c8; font-weight: 600; background: rgba(73, 151, 208, 0.06); white-space: nowrap; }
td { padding: 11px 12px; border-top: 1px solid rgba(73, 151, 208, 0.08); color: #cddcee; vertical-align: middle; }
tbody tr:hover td { background: rgba(73, 151, 208, 0.04); }

.user-cell { display: flex; align-items: center; gap: 10px; }
.user-cell strong { display: block; color: #eaf1fb; }
.user-cell small { display: block; color: #6682a7; font-size: 11px; }
.avatar {
  width: 34px; height: 34px; flex: none; display: grid; place-items: center; border-radius: 50%;
  background: linear-gradient(135deg, #2f6fb8, #4997d0); color: white; font-weight: 800; font-size: 13px;
}

.role { padding: 3px 10px; border-radius: 999px; background: rgba(73, 151, 208, 0.15); color: #9cc6ea; font-size: 11px; font-weight: 700; white-space: nowrap; }
.status { padding: 3px 10px; border-radius: 999px; font-size: 11px; font-weight: 700; }
.status.active { background: rgba(34, 197, 94, 0.15); color: #86efac; }
.status.inactive { background: rgba(251, 146, 60, 0.15); color: #fdba74; }

.actions { display: flex; gap: 6px; flex-wrap: wrap; }
.action-button { border: 0; border-radius: 8px; padding: 6px 10px; font-size: 12px; font-weight: 600; background: rgba(73, 151, 208, 0.12); color: #cddcee; }
.action-button.danger { background: rgba(248, 113, 113, 0.15); color: #fca5a5; }
.action-button.activate { background: rgba(34, 197, 94, 0.15); color: #86efac; }

.modal-backdrop {
  position: fixed; inset: 0; z-index: 50; display: grid; place-items: center; padding: 16px;
  background: rgba(2, 6, 15, 0.7); backdrop-filter: blur(3px);
}
.modal {
  width: min(620px, 100%); max-height: calc(100vh - 32px); overflow-y: auto; padding: 24px; border-radius: 16px;
  background: #0a1528; border: 1px solid rgba(73, 151, 208, 0.25); box-shadow: 0 25px 70px rgba(0, 0, 0, 0.45);
}
.modal-header { display: flex; justify-content: space-between; align-items: flex-start; gap: 12px; margin-bottom: 18px; }
.modal-header h2 { margin: 0 0 4px; font-size: 18px; }
.modal-header p { margin: 0; color: #819ab9; font-size: 12.5px; }
.close-button { border: 0; background: rgba(73, 151, 208, 0.1); color: #cddcee; width: 32px; height: 32px; border-radius: 8px; font-size: 18px; }

.form-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 14px; }
.form-grid label { display: flex; flex-direction: column; gap: 6px; font-size: 12px; font-weight: 600; color: #86a5c8; }
.form-grid .full { grid-column: 1 / -1; }
.form-grid input, .form-grid select {
  padding: 10px 12px; border-radius: 10px; border: 1px solid rgba(73, 151, 208, 0.25);
  background: #0a162a; color: #eaf1fb; font: inherit; font-size: 13px; outline: none;
}
.form-grid input:focus, .form-grid select:focus { border-color: #4997d0; }
.modal-actions { display: flex; justify-content: flex-end; gap: 10px; margin-top: 20px; }

@media (max-width: 640px) {
  .page { padding: 16px; }
  .form-grid { grid-template-columns: 1fr; }
}
</style>
