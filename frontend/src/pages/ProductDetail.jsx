import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api } from '../api/client'
import { formatPrice } from '../utils/format'
import { useCart } from '../context/CartContext'
import { pulseAddButton } from '../utils/cartAnim'
import LoadingState from '../components/LoadingState'
import ApiErrorState from '../components/ApiErrorState'
import ProductReviews from '../components/ProductReviews'

export default function ProductDetail() {
  const { id } = useParams()
  const { addItem, busy, items } = useCart()
  const addBtnRef = useRef(null)
  const [product, setProduct] = useState(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [color, setColor] = useState('')
  const [qty, setQty] = useState(1)
  const [activeImage, setActiveImage] = useState(0)
  const [lightboxOpen, setLightboxOpen] = useState(false)

  const load = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      const data = await api.getProduct(id)
      setProduct(data)
      setColor(data.colors?.[0] ?? '')
      setQty(1)
      setActiveImage(0)
      setLightboxOpen(false)
    } catch (err) {
      setProduct(null)
      setError(err.message || 'Unable to load product.')
    } finally {
      setLoading(false)
    }
  }, [id])

  useEffect(() => {
    load()
  }, [load])

  useEffect(() => {
    if (!product) return

    const previousTitle = document.title
    const metaDescription = document.querySelector('meta[name="description"]')
    const previousDescription = metaDescription?.getAttribute('content') ?? null

    document.title = `${product.seoTitle || product.name} — Bagly`
    const description = product.seoDescription || product.shortDescription
    if (metaDescription && description) {
      metaDescription.setAttribute('content', description)
    }

    return () => {
      document.title = previousTitle
      if (metaDescription && previousDescription != null) {
        metaDescription.setAttribute('content', previousDescription)
      }
    }
  }, [product])

  const gallery = useMemo(() => product?.gallery ?? [], [product])
  const soldOut = product?.inStock === false
  const mainImageSrc = gallery[activeImage] || product?.image
  const inCart = useMemo(
    () => Boolean(product && items.some((item) => item.id === product.id)),
    [items, product],
  )

  useEffect(() => {
    if (!lightboxOpen) return

    const onKeyDown = (event) => {
      if (event.key === 'Escape') setLightboxOpen(false)
    }

    document.body.style.overflow = 'hidden'
    window.addEventListener('keydown', onKeyDown)
    return () => {
      document.body.style.overflow = ''
      window.removeEventListener('keydown', onKeyDown)
    }
  }, [lightboxOpen])

  if (loading) {
    return (
      <div className="container">
        <LoadingState message="Loading product…" />
      </div>
    )
  }

  if (error) {
    return (
      <div className="container">
        <ApiErrorState
          title="Couldn't load this bag"
          message={error}
          onRetry={load}
        >
          <Link to="/shop" className="btn btn-secondary">
            Back to shop
          </Link>
        </ApiErrorState>
      </div>
    )
  }

  if (!product) {
    return (
      <div className="container empty-state">
        <h2>Bag not found</h2>
        <p>This product is no longer available.</p>
        <Link to="/shop" className="btn btn-primary">
          Back to shop
        </Link>
      </div>
    )
  }

  const handleAdd = async () => {
    if (soldOut) return
    try {
      const sourceRect = addBtnRef.current?.getBoundingClientRect() ?? null
      await addItem(product, { color, quantity: qty, sourceRect })
      pulseAddButton(addBtnRef.current)
    } catch {
      // error surfaced via cart context
    }
  }

  return (
    <section className="section pdp-page">
      <div className="container">
        <nav className="pdp-breadcrumb" aria-label="Breadcrumb">
          <Link to="/shop">Shop</Link>
          <span className="pdp-breadcrumb-sep" aria-hidden="true">
            /
          </span>
          <span className="pdp-breadcrumb-current">{product.name}</span>
        </nav>

        <div className="pdp">
          <div className="pdp-gallery">
            {soldOut ? (
              <div className="pdp-main-image is-sold">
                <img src={mainImageSrc} alt={product.name} />
                <span className="sold-stamp" aria-label="Sold out">
                  Sold
                </span>
              </div>
            ) : (
              <button
                type="button"
                className="pdp-main-image is-zoomable"
                onClick={() => setLightboxOpen(true)}
                aria-label={`Zoom ${product.name}`}
              >
                <img src={mainImageSrc} alt={product.name} />
                <span className="pdp-zoom-hint" aria-hidden="true">
                  <svg viewBox="0 0 24 24" aria-hidden="true">
                    <circle cx="11" cy="11" r="7" />
                    <line x1="21" y1="21" x2="16.65" y2="16.65" />
                    <line x1="11" y1="8" x2="11" y2="14" />
                    <line x1="8" y1="11" x2="14" y2="11" />
                  </svg>
                </span>
              </button>
            )}
            {gallery.length > 1 ? (
              <div className="pdp-thumbs">
                {gallery.map((src, index) => (
                  <button
                    key={src}
                    type="button"
                    className={index === activeImage ? 'active' : ''}
                    onClick={() => setActiveImage(index)}
                  >
                    <img src={src} alt="" />
                  </button>
                ))}
              </div>
            ) : null}
          </div>

          <div className="pdp-info">
            {product.badge ? <span className="eyebrow">{product.badge}</span> : null}
            <h1>{product.name}</h1>
            <div className="pdp-rating">
              {product.reviews > 0 ? (
                <>
                  <span className="pdp-rating-star">★ {Number(product.rating).toFixed(1)}</span>
                  <span aria-hidden="true">·</span>
                  <span>
                    {product.reviews} {product.reviews === 1 ? 'review' : 'reviews'}
                  </span>
                </>
              ) : (
                <span>No reviews yet</span>
              )}
              {product.material ? (
                <>
                  <span aria-hidden="true">·</span>
                  <span>{product.material}</span>
                </>
              ) : null}
            </div>

            <div className="pdp-price">
              <span className="price">{formatPrice(product.price)}</span>
              {product.compareAt ? (
                <span className="price-compare">{formatPrice(product.compareAt)}</span>
              ) : null}
              {soldOut ? <span className="sold-pill">Sold out</span> : null}
            </div>

            <p className="pdp-desc">{product.description}</p>

            {(product.colors || []).length > 0 ? (
              <>
                <span className="option-label">Color — {color}</span>
                <div className="color-options">
                  {(product.colors || []).map((c) => (
                    <button
                      key={c}
                      type="button"
                      className={`color-chip ${color === c ? 'active' : ''}`}
                      onClick={() => setColor(c)}
                    >
                      {c}
                    </button>
                  ))}
                </div>
              </>
            ) : null}

            <div className="qty-row">
              <div>
                <span className="option-label">Quantity</span>
                <div className="qty-control">
                  <button
                    type="button"
                    onClick={() => setQty((q) => Math.max(1, q - 1))}
                    aria-label="Decrease"
                    disabled={soldOut}
                  >
                    −
                  </button>
                  <span>{qty}</span>
                  <button
                    type="button"
                    onClick={() => setQty((q) => q + 1)}
                    aria-label="Increase"
                    disabled={soldOut}
                  >
                    +
                  </button>
                </div>
              </div>
            </div>

            <div className="pdp-cart-actions">
              {inCart && !soldOut ? (
                <p className="added-to-cart" role="status">
                  <svg viewBox="0 0 24 24" aria-hidden="true">
                    <circle cx="12" cy="12" r="10" />
                    <path d="M8 12.5l2.5 2.5L16 9.5" />
                  </svg>
                  <span>Added to cart</span>
                </p>
              ) : null}
              <div className="pdp-cart-buttons">
                {soldOut ? (
                  <button type="button" className="btn btn-primary" disabled>
                    Sold out
                  </button>
                ) : (
                  <button
                    ref={addBtnRef}
                    type="button"
                    className="btn btn-primary"
                    onClick={handleAdd}
                    disabled={busy}
                  >
                    {busy ? 'Adding…' : 'Add to cart'}
                  </button>
                )}
                <Link to="/cart" className="btn btn-secondary">
                  View cart
                </Link>
              </div>
            </div>

            {(product.features || []).length > 0 ? (
              <ul className="feature-list">
                {(product.features || []).map((feature) => (
                  <li key={feature}>{feature}</li>
                ))}
              </ul>
            ) : null}
          </div>
        </div>

        <ProductReviews
          productId={product.id}
          onSummaryChange={({ averageRating, reviewCount }) => {
            setProduct((prev) =>
              prev
                ? {
                    ...prev,
                    rating: averageRating,
                    reviews: reviewCount,
                  }
                : prev,
            )
          }}
        />
      </div>

      {lightboxOpen ? (
        <div className="image-lightbox" role="dialog" aria-modal="true" aria-label={`${product.name} enlarged view`}>
          <button
            type="button"
            className="image-lightbox-backdrop"
            onClick={() => setLightboxOpen(false)}
            aria-label="Close zoom view"
          />
          <button
            type="button"
            className="image-lightbox-close"
            onClick={() => setLightboxOpen(false)}
            aria-label="Close zoom view"
          >
            ×
          </button>
          <div className="image-lightbox-panel">
            <img src={mainImageSrc} alt={product.name} />
          </div>
        </div>
      ) : null}
    </section>
  )
}
