import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react'
import { api, getSellerToken, setSellerToken } from '../api/client'

const SellerAuthContext = createContext(null)

function toUser(result) {
  return {
    id: result.id,
    email: result.email,
    name: result.name,
    businessName: result.businessName,
    status: result.status,
  }
}

export function SellerAuthProvider({ children }) {
  const [user, setUser] = useState(null)
  const [loading, setLoading] = useState(true)
  const [token, setToken] = useState(() => getSellerToken())

  useEffect(() => {
    let cancelled = false

    async function restore() {
      const existingToken = getSellerToken()
      if (!existingToken) {
        if (!cancelled) setLoading(false)
        return
      }

      try {
        const me = await api.sellerMe()
        if (!cancelled) {
          setUser(toUser(me))
          setToken(existingToken)
        }
      } catch {
        setSellerToken(null)
        if (!cancelled) {
          setUser(null)
          setToken(null)
        }
      } finally {
        if (!cancelled) setLoading(false)
      }
    }

    restore()
    return () => {
      cancelled = true
    }
  }, [])

  const applySession = useCallback((result) => {
    setSellerToken(result.token)
    setToken(result.token)
    setUser(toUser(result))
    return result
  }, [])

  const login = useCallback(
    async (email, password) => applySession(await api.sellerLogin(email, password)),
    [applySession],
  )

  const logout = useCallback(() => {
    setSellerToken(null)
    setToken(null)
    setUser(null)
  }, [])

  const refreshUser = useCallback(async () => {
    const me = await api.sellerMe()
    setUser(toUser(me))
    return me
  }, [])

  const updateProfile = useCallback(async (payload) => {
    const profile = await api.sellerUpdateProfile(payload)
    setUser((prev) =>
      prev
        ? {
            ...prev,
            name: profile.name,
            businessName: profile.businessName,
            status: profile.status,
          }
        : prev,
    )
    return profile
  }, [])

  const value = useMemo(
    () => ({
      user,
      token,
      isAuthenticated: Boolean(user),
      loading,
      login,
      logout,
      refreshUser,
      updateProfile,
    }),
    [user, token, loading, login, logout, refreshUser, updateProfile],
  )

  return <SellerAuthContext.Provider value={value}>{children}</SellerAuthContext.Provider>
}

export function useSellerAuth() {
  const ctx = useContext(SellerAuthContext)
  if (!ctx) throw new Error('useSellerAuth must be used within SellerAuthProvider')
  return ctx
}
