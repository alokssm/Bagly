import { useEffect, useRef, useState } from 'react'
import { Link, NavLink, useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import { useCart } from '../context/CartContext'
import { useCustomerAuth } from '../context/CustomerAuthContext'
import { CART_ADD_EVENT } from '../constants/events'
import { handleCartAddAnimation } from '../utils/cartAnim'
import CustomerMenu from './CustomerMenu'
import ProductSearchBar from './ProductSearchBar'

const CART_BUMP_MS = 400

const links = [
  { to: '/', label: 'Home', end: true },
  { to: '/shop?category=school-bags', label: 'School Bags', category: 'school-bags' },
  { to: '/shop?category=stationery', label: 'Stationery', category: 'stationery' },
]

export default function Navbar() {
  const { itemCount } = useCart()
  const { user, isAuthenticated, logout } = useCustomerAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [params, setParams] = useSearchParams()
  const qParam = (params.get('q') || '').trim()
  const [searchInput, setSearchInput] = useState(qParam)
  const lastPushedQ = useRef(qParam)
  const [scrolled, setScrolled] = useState(false)
  const [open, setOpen] = useState(false)
  const [cartBump, setCartBump] = useState(false)

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

  useEffect(() => {
    let bumpTimer

    const onCartAdd = (event) => {
      setCartBump(true)
      handleCartAddAnimation(event)
      window.clearTimeout(bumpTimer)
      bumpTimer = window.setTimeout(() => setCartBump(false), CART_BUMP_MS)
    }

    window.addEventListener(CART_ADD_EVENT, onCartAdd)
    return () => {
      window.removeEventListener(CART_ADD_EVENT, onCartAdd)
      window.clearTimeout(bumpTimer)
    }
  }, [])

  // On Shop, keep the header search aligned with `?q=` (filters, back/forward).
  useEffect(() => {
    if (location.pathname !== '/shop') return
    if (qParam === lastPushedQ.current) return
    lastPushedQ.current = qParam
    setSearchInput(qParam)
  }, [qParam, location.pathname])

  // Live filter while already on Shop — preserve category/subCategory query params.
  useEffect(() => {
    if (location.pathname !== '/shop') return undefined
    const handle = window.setTimeout(() => {
      const trimmed = searchInput.trim()
      if (trimmed === lastPushedQ.current) return
      lastPushedQ.current = trimmed
      setParams(
        (prev) => {
          const next = new URLSearchParams(prev)
          if (trimmed) next.set('q', trimmed)
          else next.delete('q')
          return next
        },
        { replace: true },
      )
    }, 300)
    return () => window.clearTimeout(handle)
  }, [searchInput, location.pathname, setParams])

  const applyShopQuery = (term, { replace = false } = {}) => {
    const trimmed = term.trim()
    lastPushedQ.current = trimmed
    setSearchInput(trimmed)

    if (location.pathname === '/shop') {
      const next = new URLSearchParams(params)
      if (trimmed) next.set('q', trimmed)
      else next.delete('q')
      setParams(next, { replace })
      return
    }

    navigate(trimmed ? `/shop?q=${encodeURIComponent(trimmed)}` : '/shop')
  }

  const handleSearchSubmit = (term) => {
    applyShopQuery(term)
  }

  const firstName = user?.name?.split(' ')[0] || 'there'
  const activeCategory = params.get('category')

  const linkClassName = (link) => ({ isActive }) => {
    if (link.category) {
      return location.pathname === '/shop' && activeCategory === link.category ? 'active' : undefined
    }
    return isActive ? 'active' : undefined
  }

  return (
    <header className={`navbar ${scrolled ? 'scrolled' : ''}`}>
      <div className="container">
        <div className="nav-search-row">
          <Link to="/" className="brand" onClick={() => setOpen(false)}>
            Bag<span>ly</span>
          </Link>
          <ProductSearchBar
            id="header-product-search"
            value={searchInput}
            onChange={setSearchInput}
            onSubmit={handleSearchSubmit}
            placeholder="Search bags…"
            className="product-search--header"
          />
          <div className="nav-actions">
            {isAuthenticated ? (
              <div className="customer-nav">
                <CustomerMenu name={firstName} onLogout={handleLogout} variant="desktop" />
              </div>
            ) : (
              <div className="customer-nav">
                <Link to="/login" className="profile-btn" aria-label="Sign in">
                  <span className="profile-icon" aria-hidden="true">
                    <svg
                      xmlns="http://www.w3.org/2000/svg"
                      viewBox="0 0 24 24"
                      fill="none"
                      stroke="currentColor"
                      strokeWidth="1.75"
                      strokeLinecap="round"
                      strokeLinejoin="round"
                    >
                      <circle cx="12" cy="8" r="3.25" />
                      <path d="M5.5 19.5c1.6-3.2 4-4.75 6.5-4.75s4.9 1.55 6.5 4.75" />
                    </svg>
                  </span>
                </Link>
              </div>
            )}
            <div className="nav-mobile-action">
              {isAuthenticated ? (
                <CustomerMenu name={firstName} onLogout={handleLogout} variant="mobile" />
              ) : (
                <Link
                  to="/login"
                  className="profile-btn"
                  aria-label="Sign in"
                  onClick={() => setOpen(false)}
                >
                  <span className="profile-icon" aria-hidden="true">
                    <svg
                      xmlns="http://www.w3.org/2000/svg"
                      viewBox="0 0 24 24"
                      fill="none"
                      stroke="currentColor"
                      strokeWidth="1.75"
                      strokeLinecap="round"
                      strokeLinejoin="round"
                    >
                      <circle cx="12" cy="8" r="3.25" />
                      <path d="M5.5 19.5c1.6-3.2 4-4.75 6.5-4.75s4.9 1.55 6.5 4.75" />
                    </svg>
                  </span>
                </Link>
              )}
            </div>
            <Link
              id="bagly-cart-icon"
              to="/cart"
              className={`cart-btn${cartBump ? ' cart-btn--bump' : ''}`}
              aria-label={`Cart, ${itemCount} item${itemCount === 1 ? '' : 's'}`}
              onClick={() => setOpen(false)}
            >
              <span className="cart-icon" aria-hidden="true">
                <svg
                  xmlns="http://www.w3.org/2000/svg"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="1.75"
                  strokeLinecap="round"
                  strokeLinejoin="round"
                >
                  <path d="M3 4h6.5" />
                  <path d="M6 4v4.5" />
                  <path d="M6 8.5h13l-1.3 10.5H7.3L6 8.5z" />
                  <circle cx="9" cy="20.5" r="1.5" />
                  <circle cx="17" cy="20.5" r="1.5" />
                </svg>
              </span>
              <span className="cart-count">{itemCount}</span>
            </Link>
          </div>
        </div>

        <div className="nav-inner">
          <nav className="nav-links" aria-label="Primary">
            {links.map((link) => (
              <NavLink key={link.to} to={link.to} end={link.end} className={linkClassName(link)}>
                {link.label}
              </NavLink>
            ))}
          </nav>

          <button
            type="button"
            className={`menu-toggle${open ? ' is-open' : ''}`}
            aria-label={open ? 'Close menu' : 'Open menu'}
            aria-expanded={open}
            onClick={() => setOpen((v) => !v)}
          >
            <span className="menu-toggle-bars" aria-hidden="true">
              <span />
              <span />
              <span />
            </span>
          </button>
        </div>

        <nav className={`mobile-nav ${open ? 'open' : ''}`} aria-label="Mobile">
          {links.map((link) => (
            <NavLink
              key={link.to}
              to={link.to}
              end={link.end}
              className={linkClassName(link)}
              onClick={() => setOpen(false)}
            >
              {link.label}
            </NavLink>
          ))}
        </nav>
      </div>
    </header>
  )
}
