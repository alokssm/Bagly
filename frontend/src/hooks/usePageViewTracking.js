import { useEffect } from 'react'
import { useLocation } from 'react-router-dom'
import { api } from '../api/client'

const SESSION_ID_KEY = 'bagly-visitor-session-id'
const HIT_LOG_KEY = 'bagly-hit-log'
const MAX_LOGGED_ENTRIES = 200

function getSessionId() {
  try {
    let id = sessionStorage.getItem(SESSION_ID_KEY)
    if (!id) {
      id =
        typeof crypto !== 'undefined' && crypto.randomUUID
          ? crypto.randomUUID()
          : `${Date.now()}-${Math.random().toString(36).slice(2)}`
      sessionStorage.setItem(SESSION_ID_KEY, id)
    }
    return id
  } catch {
    return null
  }
}

/** Once per tab session, per path, per calendar day — so rapid back/forward navigation or
 * re-rendering the same route doesn't spam the hit beacon. Resets naturally when the tab closes
 * (sessionStorage) or the day rolls over. */
function alreadyLoggedToday(path) {
  try {
    const today = new Date().toISOString().slice(0, 10)
    const key = `${today}:${path}`
    const raw = sessionStorage.getItem(HIT_LOG_KEY)
    const seen = raw ? JSON.parse(raw) : []
    if (seen.includes(key)) return true
    seen.push(key)
    sessionStorage.setItem(HIT_LOG_KEY, JSON.stringify(seen.slice(-MAX_LOGGED_ENTRIES)))
    return false
  } catch {
    return false
  }
}

/** Fires one quiet, best-effort page-view beacon per storefront navigation. Never blocks
 * rendering and never surfaces errors — analytics must not affect the shopping experience. */
export default function usePageViewTracking() {
  const location = useLocation()

  useEffect(() => {
    const path = location.pathname
    if (alreadyLoggedToday(path)) return

    const sessionId = getSessionId()
    api.recordSiteHit({ path, sessionId })
  }, [location.pathname])
}
