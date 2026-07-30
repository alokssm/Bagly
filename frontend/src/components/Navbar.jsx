import { useEffect, useState } from 'react'
import { Link, NavLink } from 'react-router-dom'
import { useCart } from '../context/CartContext'
import { useAuth } from '../context/AuthContext'

const links = [
  { to: '/', label: 'Home', end: true },
  { to: '/shop', label: 'Shop' },
  { to: '/about', label: 'About' },
]

export default function Navbar() {
  const { itemCount } = useCart()
  const { isAdmin } = useAuth()
  const [scrolled, setScrolled] = useState(false)
  const [open, setOpen] = useState(false)

  useEffect(() => {
    const onScroll = () => setScrolled(window.scrollY > 12)
    onScroll()
    window.addEventListener('scroll', onScroll, { passive: true })
    return () => window.removeEventListener('scroll', onScroll)
  }, [])

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
            <Link to={isAdmin ? '/admin' : '/admin/login'} className="btn-ghost">
              {isAdmin ? 'Admin' : 'Admin login'}
            </Link>
            <Link to="/cart" className="cart-btn" aria-label={`Cart with ${itemCount} items`}>
              Cart
              <span className="cart-count">{itemCount}</span>
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
          <NavLink to={isAdmin ? '/admin' : '/admin/login'} onClick={() => setOpen(false)}>
            {isAdmin ? 'Admin' : 'Admin login'}
          </NavLink>
        </nav>
      </div>
    </header>
  )
}
