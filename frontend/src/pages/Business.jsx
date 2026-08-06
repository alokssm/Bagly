import { useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'

export default function Business() {
  const [name, setName] = useState('')
  const [businessName, setBusinessName] = useState('')
  const [email, setEmail] = useState('')
  const [phone, setPhone] = useState('')
  const [password, setPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [error, setError] = useState('')
  const [success, setSuccess] = useState('')
  const [registeredEmail, setRegisteredEmail] = useState('')
  const [submitting, setSubmitting] = useState(false)

  const onSubmit = async (e) => {
    e.preventDefault()
    setError('')
    setSuccess('')
    setRegisteredEmail('')

    if (password !== confirmPassword) {
      setError('Passwords do not match.')
      return
    }

    setSubmitting(true)
    try {
      const trimmedEmail = email.trim()
      const result = await api.sellerRegister(
        name.trim(),
        businessName.trim(),
        trimmedEmail,
        phone.trim() || null,
        password,
        confirmPassword,
      )
      setSuccess(
        result?.message ||
          'Your seller account has been created. You can sign in now.',
      )
      setRegisteredEmail(trimmedEmail)
      setName('')
      setBusinessName('')
      setEmail('')
      setPhone('')
      setPassword('')
      setConfirmPassword('')
    } catch (err) {
      setError(err.message || 'Registration failed.')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="auth-page">
      <form className="auth-card" onSubmit={onSubmit}>
        <p className="eyebrow">Sell on Bagly</p>
        <h1>Create a business account</h1>
        <p className="auth-copy">
          Join Bagly as a seller. After you register, sign in to complete your business
          details — product listing opens once an admin approves your account.
        </p>

        {error ? <p className="admin-error">{error}</p> : null}
        {success ? (
          <div className="auth-success-block">
            <p className="auth-success">{success}</p>
            <Link
              to="/business/login"
              state={{
                email: registeredEmail,
                message: 'Account created — sign in with your email and password.',
              }}
              className="btn btn-brass btn-block"
            >
              Go to seller login
            </Link>
          </div>
        ) : null}

        <div className="form-field">
          <label htmlFor="seller-name">Your name</label>
          <input
            id="seller-name"
            type="text"
            required
            value={name}
            onChange={(e) => setName(e.target.value)}
            autoComplete="name"
          />
        </div>

        <div className="form-field">
          <label htmlFor="seller-business">Business name</label>
          <input
            id="seller-business"
            type="text"
            required
            value={businessName}
            onChange={(e) => setBusinessName(e.target.value)}
            autoComplete="organization"
          />
        </div>

        <div className="form-field">
          <label htmlFor="seller-email">Email</label>
          <input
            id="seller-email"
            type="email"
            required
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            autoComplete="username"
          />
        </div>

        <div className="form-field">
          <label htmlFor="seller-phone">Phone <span className="optional">(optional)</span></label>
          <input
            id="seller-phone"
            type="tel"
            value={phone}
            onChange={(e) => setPhone(e.target.value)}
            autoComplete="tel"
          />
        </div>

        <div className="form-field">
          <label htmlFor="seller-password">Password</label>
          <input
            id="seller-password"
            type="password"
            required
            minLength={8}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            autoComplete="new-password"
          />
        </div>

        <div className="form-field">
          <label htmlFor="seller-confirm">Confirm password</label>
          <input
            id="seller-confirm"
            type="password"
            required
            minLength={8}
            value={confirmPassword}
            onChange={(e) => setConfirmPassword(e.target.value)}
            autoComplete="new-password"
          />
        </div>

        <button type="submit" className="btn btn-primary btn-block" disabled={submitting}>
          {submitting ? 'Creating account…' : 'Create business account'}
        </button>

        <p className="auth-switch">
          Already registered?{' '}
          <Link to="/business/login">Seller sign in</Link>
        </p>
        <p className="admin-login-hint">
          <Link to="/">← Back to store</Link>
        </p>
      </form>
    </div>
  )
}
