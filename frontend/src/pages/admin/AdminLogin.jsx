import { useState } from 'react'
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../../context/AuthContext'

export default function AdminLogin() {
  const { login, isAdmin, loading } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [email, setEmail] = useState('admin@bagly.store')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)

  if (!loading && isAdmin) {
    return <Navigate to={location.state?.from || '/admin'} replace />
  }

  const onSubmit = async (e) => {
    e.preventDefault()
    setSubmitting(true)
    setError('')
    try {
      await login(email.trim(), password)
      navigate(location.state?.from || '/admin', { replace: true })
    } catch (err) {
      setError(err.message || 'Login failed.')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="admin-login-page">
      <form className="admin-login-card" onSubmit={onSubmit}>
        <p className="eyebrow">Admin</p>
        <h1>Bagly console</h1>
        <p className="admin-login-copy">Sign in to manage products and categories.</p>

        {error ? <p className="admin-error">{error}</p> : null}

        <div className="form-field">
          <label htmlFor="admin-email">Email</label>
          <input
            id="admin-email"
            type="email"
            required
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            autoComplete="username"
          />
        </div>

        <div className="form-field">
          <label htmlFor="admin-password">Password</label>
          <input
            id="admin-password"
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

        <p className="admin-login-hint">
          <br />
          <Link to="/">← Back to store</Link>
        </p>
      </form>
    </div>
  )
}
