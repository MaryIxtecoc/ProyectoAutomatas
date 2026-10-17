// frontend/nuxt.config.ts
export default defineNuxtConfig({
  ssr: false,

  nitro: {
    preset: 'vercel-static'
  },

  runtimeConfig: {
    public: {
      apiUrl: process.env.NUXT_PUBLIC_API_URL || 'https://proyectoautomatas-1bk6.onrender.com'
    }
  }
})