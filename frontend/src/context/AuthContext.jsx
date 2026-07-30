import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react'
import { api, getAuthToken, setAuthToken } from '../api/client'

const AuthContext = createContext(null)

export function AuthProvider({ children }) {
  const [admin, setAdmin] = useState(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    let cancelled = false

    async function restore() {
      const token = getAuthToken()
      if (!token) {
        if (!cancelled) setLoading(false)
        return
      }

      try {
        const me = await api.me()
        if (!cancelled) setAdmin({ email: me.email, name: me.name, role: me.role })
      } catch {
        setAuthToken(null)
        if (!cancelled) setAdmin(null)
      } finally {
        if (!cancelled) setLoading(false)
      }
    }

    restore()
    return () => {
      cancelled = true
    }
  }, [])

  const login = useCallback(async (email, password) => {
    const result = await api.login(email, password)
    setAuthToken(result.token)
    setAdmin({ email: result.email, name: result.name, role: result.role })
    return result
  }, [])

  const logout = useCallback(async () => {
    try {
      if (getAuthToken()) {
        await api.logout()
      }
    } catch {
      // still clear local session even if API logout fails
    } finally {
      setAuthToken(null)
      setAdmin(null)
    }
  }, [])

  const value = useMemo(
    () => ({
      admin,
      isAdmin: Boolean(admin),
      loading,
      login,
      logout,
    }),
    [admin, loading, login, logout],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used within AuthProvider')
  return ctx
}
