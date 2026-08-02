import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react'
import { api, getCustomerToken, getGoogleAuthConfig, setCustomerToken } from '../api/client'
import { CUSTOMER_LOGOUT_EVENT } from '../constants/events'

export { getGoogleAuthConfig }

const CustomerAuthContext = createContext(null)

export function CustomerAuthProvider({ children }) {
  const [user, setUser] = useState(null)
  const [loading, setLoading] = useState(true)
  const [token, setToken] = useState(() => getCustomerToken())

  useEffect(() => {
    let cancelled = false

    async function restore() {
      const existingToken = getCustomerToken()
      if (!existingToken) {
        if (!cancelled) setLoading(false)
        return
      }

      try {
        const me = await api.customerMe()
        if (!cancelled) {
          setUser(me)
          setToken(existingToken)
        }
      } catch {
        setCustomerToken(null)
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
    setCustomerToken(result.token)
    setToken(result.token)
    setUser({ id: result.id, email: result.email, name: result.name })
    return result
  }, [])

  const register = useCallback(
    async (name, email, password, confirmPassword) =>
      applySession(await api.customerRegister(name, email, password, confirmPassword)),
    [applySession],
  )

  const login = useCallback(
    async (email, password) => applySession(await api.customerLogin(email, password)),
    [applySession],
  )

  const loginWithGoogle = useCallback(
    async (idToken) => applySession(await api.customerGoogleLogin(idToken)),
    [applySession],
  )

  const updateProfile = useCallback(
    async (name) => applySession(await api.updateCustomerProfile(name)),
    [applySession],
  )

  const logout = useCallback(() => {
    setCustomerToken(null)
    setToken(null)
    setUser(null)
    window.dispatchEvent(new Event(CUSTOMER_LOGOUT_EVENT))
  }, [])

  const value = useMemo(
    () => ({
      user,
      token,
      isAuthenticated: Boolean(user),
      loading,
      register,
      login,
      loginWithGoogle,
      updateProfile,
      logout,
    }),
    [user, token, loading, register, login, loginWithGoogle, updateProfile, logout],
  )

  return <CustomerAuthContext.Provider value={value}>{children}</CustomerAuthContext.Provider>
}

export function useCustomerAuth() {
  const ctx = useContext(CustomerAuthContext)
  if (!ctx) throw new Error('useCustomerAuth must be used within CustomerAuthProvider')
  return ctx
}
