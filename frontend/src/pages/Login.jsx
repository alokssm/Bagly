import { useEffect, useRef, useState } from 'react'
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom'
import { useCustomerAuth } from '../context/CustomerAuthContext'
import { getGoogleAuthConfig } from '../api/client'
import { loadGoogleIdentityScript } from '../utils/googleAuth'

const LOCAL_GOOGLE_CLIENT_ID = import.meta.env.VITE_GOOGLE_CLIENT_ID

export default function Login() {
  const { login, loginWithGoogle, isAuthenticated, loading } = useCustomerAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [googleClientId, setGoogleClientId] = useState(null)
  const [googleConfigLoading, setGoogleConfigLoading] = useState(true)
  const googleBtnRef = useRef(null)

  useEffect(() => {
    let cancelled = false

    getGoogleAuthConfig()
      .then((cfg) => {
        if (cancelled) return
        const clientId =
          cfg?.enabled && cfg?.clientId ? cfg.clientId : LOCAL_GOOGLE_CLIENT_ID || null
        setGoogleClientId(clientId)
      })
      .catch(() => {
        if (!cancelled) setGoogleClientId(LOCAL_GOOGLE_CLIENT_ID || null)
      })
      .finally(() => {
        if (!cancelled) setGoogleConfigLoading(false)
      })

    return () => {
      cancelled = true
    }
  }, [])

  useEffect(() => {
    if (!googleClientId) return
    let cancelled = false

    loadGoogleIdentityScript()
      .then(() => {
        if (cancelled || !window.google?.accounts?.id) return

        window.google.accounts.id.initialize({
          client_id: googleClientId,
          callback: async (response) => {
            setError('')
            try {
              await loginWithGoogle(response.credential)
              navigate(location.state?.from || '/', { replace: true })
            } catch (err) {
              setError(err.message || 'Google sign-in failed.')
            }
          },
        })

        if (googleBtnRef.current) {
          window.google.accounts.id.renderButton(googleBtnRef.current, {
            theme: 'outline',
            size: 'large',
            width: 320,
            text: 'continue_with',
          })
        }
      })
      .catch(() => {
        setError((prev) => prev || 'Could not load Google sign-in. Please try email/password.')
      })

    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [googleClientId])

  if (!loading && isAuthenticated) {
    return <Navigate to={location.state?.from || '/'} replace />
  }

  const onSubmit = async (e) => {
    e.preventDefault()
    setSubmitting(true)
    setError('')
    try {
      await login(email.trim(), password)
      navigate(location.state?.from || '/', { replace: true })
    } catch (err) {
      setError(err.message || 'Login failed.')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="auth-page">
      <form className="auth-card" onSubmit={onSubmit}>
        <p className="eyebrow">Welcome back</p>
        <h1>Sign in to Bagly</h1>
        <p className="auth-copy">Sign in to chat with us and keep track of your orders.</p>

        {error ? <p className="admin-error">{error}</p> : null}

        <div className="form-field">
          <label htmlFor="login-email">Email</label>
          <input
            id="login-email"
            type="email"
            required
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            autoComplete="username"
          />
        </div>

        <div className="form-field">
          <label htmlFor="login-password">Password</label>
          <input
            id="login-password"
            type="password"
            required
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            autoComplete="current-password"
          />
        </div>

        <button type="submit" className="btn btn-primary btn-block" disabled={submitting}>
          {submitting ? 'Signing in…' : 'Sign in'}
        </button>

        {googleConfigLoading ? null : googleClientId ? (
          <>
            <div className="auth-divider">
              <span>or</span>
            </div>
            <div ref={googleBtnRef} className="google-btn-slot" />
          </>
        ) : (
          <p className="auth-hint">Google sign-in isn't configured yet.</p>
        )}

        <p className="auth-switch">
          New to Bagly? <Link to="/register">Create an account</Link>
        </p>
        <p className="admin-login-hint">
          <Link to="/">← Back to store</Link>
        </p>
      </form>
    </div>
  )
}
