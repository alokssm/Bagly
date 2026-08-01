import { useEffect, useState } from 'react'

const DISMISS_KEY = 'bagly-pwa-install-dismissed'

export default function InstallPrompt() {
  const [deferredPrompt, setDeferredPrompt] = useState(null)
  const [dismissed, setDismissed] = useState(
    () => localStorage.getItem(DISMISS_KEY) === '1',
  )

  useEffect(() => {
    const onBeforeInstall = (event) => {
      event.preventDefault()
      setDeferredPrompt(event)
    }

    window.addEventListener('beforeinstallprompt', onBeforeInstall)
    return () => window.removeEventListener('beforeinstallprompt', onBeforeInstall)
  }, [])

  if (dismissed || !deferredPrompt) return null

  const dismiss = () => {
    localStorage.setItem(DISMISS_KEY, '1')
    setDismissed(true)
  }

  const install = async () => {
    deferredPrompt.prompt()
    await deferredPrompt.userChoice
    setDeferredPrompt(null)
    dismiss()
  }

  return (
    <div className="install-prompt" role="region" aria-label="Install Bagly app">
      <p className="install-prompt__text">
        Install Bagly for quick access from your home screen.
      </p>
      <div className="install-prompt__actions">
        <button type="button" className="install-prompt__install" onClick={install}>
          Install
        </button>
        <button type="button" className="install-prompt__dismiss" onClick={dismiss}>
          Not now
        </button>
      </div>
    </div>
  )
}
