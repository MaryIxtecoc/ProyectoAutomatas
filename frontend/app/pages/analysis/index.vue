<script setup lang="ts">
// El listado de análisis vive en el dashboard.
definePageMeta({ middleware: 'auth' })

await navigateTo('/dashboard', { replace: true })
</script>

<template>
  <div />
</template>
