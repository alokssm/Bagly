import { Link } from 'react-router-dom'
import { useSellerAuth } from '../context/SellerAuthContext'

/** Stub home after seller login — full profile/products come later. */
export default function SellerDashboard() {
  const { user, logout } = useSellerAuth()
  const displayName = user?.name?.split(' ')[0] || 'seller'

  return (
    <div className="auth-page">
      <div className="auth-card">
        <p className="eyebrow">Seller hub</p>
        <h1>Welcome, {displayName}</h1>
        <p className="auth-copy">
          You&apos;re signed in
          {user?.businessName ? (
            <>
              {' '}
              as <strong>{user.businessName}</strong>
            </>
          ) : null}
          . Profile details and product tools are coming soon.
        </p>
        {user?.status ? (
          <p className="auth-hint">
            Account status: <strong>{user.status}</strong>
            {user.status === 'Pending'
              ? ' — you can explore the hub now; listing products will open after approval.'
              : null}
          </p>
        ) : null}
        <button type="button" className="btn btn-ghost btn-block" onClick={logout}>
          Sign out
        </button>
        <p className="admin-login-hint">
          <Link to="/">← Back to store</Link>
        </p>
      </div>
    </div>
  )
}
