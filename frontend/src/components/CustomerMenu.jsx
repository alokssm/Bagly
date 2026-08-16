import { useEffect, useRef, useState } from 'react'
import { Link, useLocation } from 'react-router-dom'

function ProfileIcon() {
  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.75"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <circle cx="12" cy="8" r="3.25" />
      <path d="M5.5 19.5c1.6-3.2 4-4.75 6.5-4.75s4.9 1.55 6.5 4.75" />
    </svg>
  )
}

/** Profile icon dropdown consolidating Profile / Orders / Addresses / Sign out. */
export default function CustomerMenu({ name, onLogout, variant = 'desktop' }) {
  const [open, setOpen] = useState(false)
  const containerRef = useRef(null)
  const location = useLocation()

  useEffect(() => {
    setOpen(false)
  }, [location.pathname])

  useEffect(() => {
    if (!open) return

    const onClickOutside = (e) => {
      if (containerRef.current && !containerRef.current.contains(e.target)) {
        setOpen(false)
      }
    }
    const onKeyDown = (e) => {
      if (e.key === 'Escape') setOpen(false)
    }

    document.addEventListener('mousedown', onClickOutside)
    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('mousedown', onClickOutside)
      document.removeEventListener('keydown', onKeyDown)
    }
  }, [open])

  const close = () => setOpen(false)
  const displayName = name ? name.charAt(0).toUpperCase() + name.slice(1) : 'Account'

  return (
    <div className={`customer-menu customer-menu--${variant}`} ref={containerRef}>
      <button
        type="button"
        className="customer-menu__trigger profile-btn"
        onClick={() => setOpen((v) => !v)}
        aria-label={`Account menu for ${displayName}`}
        aria-haspopup="menu"
        aria-expanded={open}
      >
        <span className="profile-icon">
          <ProfileIcon />
        </span>
      </button>

      {open ? (
        <div className="customer-menu__dropdown" role="menu">
          <Link to="/profile" role="menuitem" onClick={close}>
            Profile
          </Link>
          <Link to="/orders" role="menuitem" onClick={close}>
            Orders
          </Link>
          <Link to="/addresses" role="menuitem" onClick={close}>
            Shipping addresses
          </Link>
          <button
            type="button"
            role="menuitem"
            className="customer-menu__signout"
            onClick={() => {
              close()
              onLogout()
            }}
          >
            Sign out
          </button>
        </div>
      ) : null}
    </div>
  )
}
