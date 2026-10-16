
export default defineNuxtConfig({
  ssr: false,
  nitro: {
    prerender: {
      crawlLinks: false,
      failOnError: false
    }
  },
  runtimeConfig: {
    public: {
      apiUrl: 'https://proyectoautomatas-1bk6.onrender.com'
    }
  }
})