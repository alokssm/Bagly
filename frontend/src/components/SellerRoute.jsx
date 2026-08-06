import { Navigate, useLocation } from 'react-router-dom'
import { useSellerAuth } from '../context/SellerAuthContext'

/** Gates seller pages behind a logged-in seller session. */
export default function SellerRoute({ children }) {
  const { isAuthenticated, loading } = useSellerAuth()
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
        to="/business/login"
        replace
        state={{ from: location.pathname, message: 'Sign in to your seller account.' }}
      />
    )
  }

  return children
}
