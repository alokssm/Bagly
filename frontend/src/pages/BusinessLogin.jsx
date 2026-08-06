import { useState } from 'react'
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom'
import { useSellerAuth } from '../context/SellerAuthContext'

export default function BusinessLogin() {
  const { login, isAuthenticated, loading } = useSellerAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [email, setEmail] = useState(() => location.state?.email || '')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)

  if (!loading && isAuthenticated) {
    return <Navigate to={location.state?.from || '/seller'} replace />
  }

  const onSubmit = async (e) => {
    e.preventDefault()
    setSubmitting(true)
    setError('')
    try {
      await login(email.trim(), password)
      navigate(location.state?.from || '/seller', { replace: true })
    } catch (err) {
      setError(err.message || 'Login failed.')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="auth-page">
      <form className="auth-card" onSubmit={onSubmit}>
        <p className="eyebrow">Sell on Bagly</p>
        <h1>Seller sign in</h1>
        <p className="auth-copy">
          {location.state?.message || 'Sign in to your seller account to manage your Bagly business.'}
        </p>

        {error ? <p className="admin-error">{error}</p> : null}

        <div className="form-field">
          <label htmlFor="seller-login-email">Email</label>
          <input
            id="seller-login-email"
            type="email"
            required
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            autoComplete="username"
          />
        </div>

        <div className="form-field">
          <label htmlFor="seller-login-password">Password</label>
          <input
            id="seller-login-password"
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

        <p className="auth-switch">
          New seller?{' '}
          <Link to="/business">Create a business account</Link>
        </p>
        <p className="admin-login-hint">
          <Link to="/">← Back to store</Link>
        </p>
      </form>
    </div>
  )
}
