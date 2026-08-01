import { useEffect, useRef, useState } from 'react'
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom'
import { useCustomerAuth } from '../context/CustomerAuthContext'
import { getGoogleAuthConfig } from '../api/client'
import { loadGoogleIdentityScript } from '../utils/googleAuth'

const LOCAL_GOOGLE_CLIENT_ID = import.meta.env.VITE_GOOGLE_CLIENT_ID

export default function Register() {
  const { register, loginWithGoogle, isAuthenticated, loading } = useCustomerAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [name, setName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
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
            text: 'signup_with',
          })
        }
      })
      .catch(() => {
        setError((prev) => prev || 'Could not load Google sign-in. Please use the form below.')
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
    setError('')

    if (password !== confirmPassword) {
      setError('Passwords do not match.')
      return
    }

    setSubmitting(true)
    try {
      await register(name.trim(), email.trim(), password, confirmPassword)
      navigate(location.state?.from || '/', { replace: true })
    } catch (err) {
      setError(err.message || 'Registration failed.')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="auth-page">
      <form className="auth-card" onSubmit={onSubmit}>
        <p className="eyebrow">Join Bagly</p>
        <h1>Create your account</h1>
        <p className="auth-copy">
          {location.state?.message || 'Register to chat with us and track your orders.'}
        </p>

        {error ? <p className="admin-error">{error}</p> : null}

        <div className="form-field">
          <label htmlFor="register-name">Name</label>
          <input
            id="register-name"
            type="text"
            required
            value={name}
            onChange={(e) => setName(e.target.value)}
            autoComplete="name"
          />
        </div>

        <div className="form-field">
          <label htmlFor="register-email">Email</label>
          <input
            id="register-email"
            type="email"
            required
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            autoComplete="username"
          />
        </div>

        <div className="form-field">
          <label htmlFor="register-password">Password</label>
          <input
            id="register-password"
            type="password"
            required
            minLength={8}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            autoComplete="new-password"
          />
        </div>

        <div className="form-field">
          <label htmlFor="register-confirm">Confirm password</label>
          <input
            id="register-confirm"
            type="password"
            required
            minLength={8}
            value={confirmPassword}
            onChange={(e) => setConfirmPassword(e.target.value)}
            autoComplete="new-password"
          />
        </div>

        <button type="submit" className="btn btn-primary btn-block" disabled={submitting}>
          {submitting ? 'Creating account…' : 'Create account'}
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
          Already have an account?{' '}
          <Link to="/login" state={location.state}>
            Sign in
          </Link>
        </p>
        <p className="admin-login-hint">
          <Link to="/">← Back to store</Link>
        </p>
      </form>
    </div>
  )
}
