// Carga el script de reCAPTCHA una sola vez y expone render()/reset() para
// usarlo desde cualquier página. Nuxt lo auto-importa desde app/composables.
//
// render() se puede llamar más de una vez con contenedores distintos (por
// ejemplo si la página vuelve atrás y adelante entre pasos, recreando el
// <div>): si el contenedor es uno nuevo, se renderiza un widget fresco en
// vez de quedarse apuntando a un <div> que ya no existe en el DOM.
export function useRecaptcha() {
  const token = ref('')
  let widgetId: number | null = null
  let renderedContainer: HTMLElement | null = null

  function loadScript(): Promise<void> {
    return new Promise((resolve) => {
      const w = window as any

      if (w.grecaptcha?.render) {
        resolve()
        return
      }

      const existing = document.getElementById('recaptcha-script')
      if (existing) {
        existing.addEventListener('load', () => resolve())
        return
      }

      const script = document.createElement('script')
      script.id = 'recaptcha-script'
      script.src = 'https://www.google.com/recaptcha/api.js'
      script.async = true
      script.defer = true
      script.onload = () => resolve()
      document.head.appendChild(script)
    })
  }

  async function render(container: HTMLElement) {
    if (widgetId !== null && renderedContainer === container) {
      return
    }

    await loadScript()

    const w = window as any
    const siteKey = useRuntimeConfig().public.recaptchaSiteKey as string

    widgetId = null
    token.value = ''
    renderedContainer = container

    const tryRender = () => {
      if (renderedContainer !== container) return // se pidió render() de otro contenedor mientras tanto

      if (w.grecaptcha?.render) {
        widgetId = w.grecaptcha.render(container, {
          sitekey: siteKey,
          callback: (value: string) => { token.value = value },
          'expired-callback': () => { token.value = '' }
        })
      } else {
        setTimeout(tryRender, 150)
      }
    }

    tryRender()
  }

  function reset() {
    const w = window as any
    if (widgetId !== null && w.grecaptcha?.reset) {
      w.grecaptcha.reset(widgetId)
    }
    token.value = ''
  }

  return { token, render, reset }
}
