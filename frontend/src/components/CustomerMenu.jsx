import { useEffect, useRef, useState } from 'react'
import { Link, useLocation } from 'react-router-dom'

/** Single "Hi, {name}" dropdown consolidating Profile / Orders / Addresses / Sign out. */
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

  return (
    <div className={`customer-menu customer-menu--${variant}`} ref={containerRef}>
      <button
        type="button"
        className="customer-menu__trigger"
        onClick={() => setOpen((v) => !v)}
        aria-haspopup="menu"
        aria-expanded={open}
      >
        Hi, {name}
        <span className={`customer-menu__chevron ${open ? 'open' : ''}`} aria-hidden="true">
          ▾
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
