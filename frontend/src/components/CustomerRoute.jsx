import { Navigate, useLocation } from 'react-router-dom'
import { useCustomerAuth } from '../context/CustomerAuthContext'

/** Gates storefront pages (e.g. /orders) behind a logged-in customer session. */
export default function CustomerRoute({ children }) {
  const { isAuthenticated, loading } = useCustomerAuth()
  const location = useLocation()

  if (loading) {
    return (
      <div className="container empty-state">
        <h2>Loading…</h2>
      </div>
    )
  }

  if (!isAuthenticated) {
    return (
      <Navigate
        to="/login"
        replace
        state={{ from: location.pathname, message: 'Sign in to view your orders.' }}
      />
    )
  }

  return children
}
