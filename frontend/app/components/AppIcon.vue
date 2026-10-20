<script setup lang="ts">
// Íconos animados del proyecto (public/icons/*.webp) en lugar de emojis.
defineProps<{
  name: 'camara' | 'escanear-qr' | 'imagen'
  size?: number
}>()
</script>

<template>
  <img
    :src="`/icons/${name}.webp`"
    :width="size ?? 22"
    :height="size ?? 22"
    class="app-icon"
    alt=""
    aria-hidden="true"
  >
</template>

<style scoped>
.app-icon {
  display: inline-block;
  vertical-align: middle;
  object-fit: contain;
  flex: none;
}
</style>
