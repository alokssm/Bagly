import { useEffect, useState } from 'react'
import { Link, NavLink, useNavigate } from 'react-router-dom'
import { useCart } from '../context/CartContext'
import { useAuth } from '../context/AuthContext'
import { useCustomerAuth } from '../context/CustomerAuthContext'
import CustomerMenu from './CustomerMenu'

const links = [
  { to: '/', label: 'Home', end: true },
  { to: '/shop', label: 'Shop' },
  { to: '/about', label: 'About' },
]

export default function Navbar() {
  const { itemCount } = useCart()
  const { isAdmin } = useAuth()
  const { user, isAuthenticated, logout } = useCustomerAuth()
  const navigate = useNavigate()
  const [scrolled, setScrolled] = useState(false)
  const [open, setOpen] = useState(false)

  const handleLogout = () => {
    logout()
    setOpen(false)
    navigate('/')
  }

  useEffect(() => {
    const onScroll = () => setScrolled(window.scrollY > 12)
    onScroll()
    window.addEventListener('scroll', onScroll, { passive: true })
    return () => window.removeEventListener('scroll', onScroll)
  }, [])

  const firstName = user?.name?.split(' ')[0] || 'there'

  return (
    <header className={`navbar ${scrolled ? 'scrolled' : ''}`}>
      <div className="container">
        <div className="nav-inner">
          <Link to="/" className="brand" onClick={() => setOpen(false)}>
            Bag<span>ly</span>
          </Link>

          <nav className="nav-links" aria-label="Primary">
            {links.map((link) => (
              <NavLink key={link.to} to={link.to} end={link.end}>
                {link.label}
              </NavLink>
            ))}
          </nav>

          <div className="nav-actions">
            {isAuthenticated ? (
              <div className="customer-nav">
                <CustomerMenu name={firstName} onLogout={handleLogout} variant="desktop" />
              </div>
            ) : (
              <div className="customer-nav">
                <Link to="/login" className="btn-ghost">
                  Sign in
                </Link>
                <Link to="/register" className="btn btn-brass btn-sm">
                  Create Account
                </Link>
              </div>
            )}
            <div className="nav-mobile-action">
              {isAuthenticated ? (
                <CustomerMenu name={firstName} onLogout={handleLogout} variant="mobile" />
              ) : (
                <Link to="/login" className="nav-mobile-action-link" onClick={() => setOpen(false)}>
                  Sign in
                </Link>
              )}
            </div>
            <Link
              to="/cart"
              className="cart-btn"
              aria-label={itemCount > 0 ? `Cart with ${itemCount} items` : 'Cart'}
              onClick={() => setOpen(false)}
            >
              Cart
              {itemCount > 0 && <span className="cart-count">{itemCount}</span>}
            </Link>
            <button
              type="button"
              className="menu-toggle"
              aria-label="Toggle menu"
              aria-expanded={open}
              onClick={() => setOpen((v) => !v)}
            >
              {open ? '✕' : '☰'}
            </button>
          </div>
        </div>

        <nav className={`mobile-nav ${open ? 'open' : ''}`} aria-label="Mobile">
          {links.map((link) => (
            <NavLink key={link.to} to={link.to} end={link.end} onClick={() => setOpen(false)}>
              {link.label}
            </NavLink>
          ))}
          {!isAuthenticated ? (
            <NavLink to="/register" onClick={() => setOpen(false)}>
              Create Account
            </NavLink>
          ) : null}
          {/* <NavLink to={isAdmin ? '/admin' : '/admin/login'} onClick={() => setOpen(false)}>
            {isAdmin ? 'Admin' : 'Admin login'}
          </NavLink> */}
        </nav>
      </div>
    </header>
  )
}
