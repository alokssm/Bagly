import { useEffect, useRef, useState } from 'react'
import { Link, useLocation } from 'react-router-dom'

/** Primary-nav "Business" dropdown: Sign up as seller / Seller login. */
export default function BusinessNavMenu({ onNavigate, variant = 'desktop' }) {
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

  const close = () => {
    setOpen(false)
    onNavigate?.()
  }

  const isBusinessPath =
    location.pathname === '/business' ||
    location.pathname.startsWith('/business/') ||
    location.pathname.startsWith('/seller')

  if (variant === 'mobile') {
    return (
      <>
        <Link to="/business" onClick={close}>
          Sign up as seller
        </Link>
        <Link to="/business/login" onClick={close}>
          Seller login
        </Link>
      </>
    )
  }

  return (
    <div className="business-nav-menu" ref={containerRef}>
      <button
        type="button"
        className={`business-nav-menu__trigger${isBusinessPath ? ' active' : ''}`}
        onClick={() => setOpen((v) => !v)}
        aria-haspopup="menu"
        aria-expanded={open}
      >
        Business
        <span className={`business-nav-menu__chevron ${open ? 'open' : ''}`} aria-hidden="true">
          ▾
        </span>
      </button>

      {open ? (
        <div className="business-nav-menu__dropdown" role="menu">
          <Link to="/business" role="menuitem" onClick={close}>
            Sign up as seller
          </Link>
          <Link to="/business/login" role="menuitem" onClick={close}>
            Seller login
          </Link>
        </div>
      ) : null}
    </div>
  )
}
