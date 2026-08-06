import { Link } from 'react-router-dom'

/** Placeholder until seller login + approval flow (points 2–3) ships. */
export default function BusinessLogin() {
  return (
    <div className="auth-page">
      <div className="auth-card">
        <p className="eyebrow">Sell on Bagly</p>
        <h1>Seller sign in</h1>
        <p className="auth-copy">
          Seller login is coming soon. If you just created an account, it is pending approval —
          you&apos;ll be able to sign in once approved.
        </p>
        <Link to="/business" className="btn btn-primary btn-block">
          Create a business account
        </Link>
        <p className="admin-login-hint">
          <Link to="/">← Back to store</Link>
        </p>
      </div>
    </div>
  )
}
