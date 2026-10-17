export default defineNuxtConfig({
  ssr: false,

  routeRules: {
    '/**': { ssr: false }
  },

  nitro: {
    preset: 'vercel-static',
    prerender: {
      routes: [],
      crawlLinks: false
    }
  },

  runtimeConfig: {
    public: {
      apiUrl: process.env.NUXT_PUBLIC_API_URL || 'https://proyectoautomatas-1bk6.onrender.com'
    }
  }
})