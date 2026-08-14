import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useAuth } from '../context/AuthContext'
import { getAuthToken } from '../api/client'
import AdminChatPanel from './AdminChatPanel'

export default function AdminLayout() {
  const { admin, logout } = useAuth()
  const navigate = useNavigate()

  const handleLogout = () => {
    logout()
    navigate('/admin/login')
  }

  return (
    <div className="admin-shell">
      <aside className="admin-sidebar">
        <div className="admin-brand">
          Bag<span>ly</span> Admin
        </div>
        <nav className="admin-nav">
          <NavLink to="/admin" end>
            Dashboard
          </NavLink>
          <NavLink to="/admin/categories">Categories</NavLink>
          <NavLink to="/admin/orders">Orders</NavLink>
          <NavLink to="/admin/shipping">Shipping</NavLink>
          <NavLink to="/admin/sellers">Sellers</NavLink>
          <NavLink to="/admin/analytics">Analytics</NavLink>
          <NavLink to="/admin/traffic">Traffic</NavLink>
          <NavLink to="/admin/reports">Reports</NavLink>
          <NavLink to="/" className="admin-store-link">
            View store
          </NavLink>
        </nav>
        <div className="admin-user">
          <p>{admin?.name}</p>
          <small>{admin?.email}</small>
          <button type="button" className="btn btn-secondary btn-block" onClick={handleLogout}>
            Log out
          </button>
        </div>
      </aside>
      <div className="admin-main">
        <Outlet />
      </div>
      {admin ? <AdminChatPanel token={getAuthToken()} /> : null}
    </div>
  )
}
