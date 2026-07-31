const GOOGLE_SCRIPT_SRC = 'https://accounts.google.com/gsi/client'

let scriptPromise = null

/** Loads the Google Identity Services script once and resolves when window.google.accounts.id is ready. */
export function loadGoogleIdentityScript() {
  if (typeof window === 'undefined') {
    return Promise.reject(new Error('No window available.'))
  }

  if (window.google?.accounts?.id) {
    return Promise.resolve()
  }

  if (scriptPromise) {
    return scriptPromise
  }

  scriptPromise = new Promise((resolve, reject) => {
    const existing = document.querySelector(`script[src="${GOOGLE_SCRIPT_SRC}"]`)
    if (existing) {
      if (window.google?.accounts?.id) {
        resolve()
        return
      }
      existing.addEventListener('load', () => resolve())
      existing.addEventListener('error', () => reject(new Error('Failed to load Google Identity script.')))
      return
    }

    const script = document.createElement('script')
    script.src = GOOGLE_SCRIPT_SRC
    script.async = true
    script.defer = true
    script.onload = () => resolve()
    script.onerror = () => reject(new Error('Failed to load Google Identity script.'))
    document.head.appendChild(script)
  })

  return scriptPromise
}
