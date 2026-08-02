import { useEffect, useMemo, useRef, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api } from '../api/client'
import { formatPrice } from '../utils/format'
import { useCart } from '../context/CartContext'
import { pulseAddButton } from '../utils/cartAnim'

export default function ProductDetail() {
  const { id } = useParams()
  const { addItem, busy } = useCart()
  const addBtnRef = useRef(null)
  const [product, setProduct] = useState(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [color, setColor] = useState('')
  const [qty, setQty] = useState(1)
  const [activeImage, setActiveImage] = useState(0)
  const [added, setAdded] = useState(false)

  useEffect(() => {
    let cancelled = false

    async function load() {
      setLoading(true)
      setError('')
      try {
        const data = await api.getProduct(id)
        if (cancelled) return
        setProduct(data)
        setColor(data.colors?.[0] ?? '')
        setQty(1)
        setActiveImage(0)
        setAdded(false)
      } catch (err) {
        if (!cancelled) {
          setProduct(null)
          setError(err.message || 'Unable to load product.')
        }
      } finally {
        if (!cancelled) setLoading(false)
      }
    }

    load()
    return () => {
      cancelled = true
    }
  }, [id])

  const gallery = useMemo(() => product?.gallery ?? [], [product])
  const soldOut = product?.inStock === false

  if (loading) {
    return (
      <div className="container empty-state">
        <h2>Loading bag…</h2>
      </div>
    )
  }

  if (!product) {
    return (
      <div className="container empty-state">
        <h2>Bag not found</h2>
        <p>{error || 'This product is no longer available.'}</p>
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
      setAdded(true)
      window.setTimeout(() => setAdded(false), 1800)
    } catch {
      // error surfaced via cart context
    }
  }

  return (
    <section className="section" style={{ paddingTop: '2rem' }}>
      <div className="container">
        <p style={{ marginBottom: '1.5rem', color: 'var(--ink-soft)' }}>
          <Link to="/shop">Shop</Link> / {product.name}
        </p>

        <div className="pdp">
          <div className="pdp-gallery">
            <div className={`pdp-main-image ${soldOut ? 'is-sold' : ''}`}>
              <img src={gallery[activeImage] || product.image} alt={product.name} />
              {soldOut ? (
                <span className="sold-stamp" aria-label="Sold out">
                  Sold
                </span>
              ) : null}
            </div>
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
              <span>★ {product.rating}</span>
              <span>·</span>
              <span>{product.reviews} reviews</span>
              <span>·</span>
              <span>{product.material}</span>
            </div>

            <div className="pdp-price">
              <span className="price">{formatPrice(product.price)}</span>
              {product.compareAt ? (
                <span className="price-compare">{formatPrice(product.compareAt)}</span>
              ) : null}
              {soldOut ? <span className="sold-pill">Sold out</span> : null}
            </div>

            <p className="pdp-desc">{product.description}</p>

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

            <div style={{ display: 'flex', flexWrap: 'wrap', gap: '0.75rem' }}>
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
                  {added ? 'Added ✓' : busy ? 'Adding…' : 'Add to cart'}
                </button>
              )}
              <Link to="/cart" className="btn btn-secondary">
                View cart
              </Link>
            </div>

            <ul className="feature-list">
              {(product.features || []).map((feature) => (
                <li key={feature}>{feature}</li>
              ))}
            </ul>
          </div>
        </div>
      </div>
    </section>
  )
}
