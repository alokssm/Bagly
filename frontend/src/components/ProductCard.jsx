import { useMemo, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { formatPrice } from '../utils/format'
import { useCart } from '../context/CartContext'
import { pulseAddButton } from '../utils/cartAnim'
import { CompactRating } from './ProductReviews'

export default function ProductCard({ product }) {
  const { addItem, items } = useCart()
  const addBtnRef = useRef(null)
  const [adding, setAdding] = useState(false)
  const [error, setError] = useState('')

  const soldOut = product.inStock === false
  const productHref = `/product/${product.slug || product.id}`
  const inCart = useMemo(
    () => items.some((item) => item.id === product.id),
    [items, product.id],
  )

  const handleAdd = async () => {
    setAdding(true)
    setError('')
    try {
      const sourceRect = addBtnRef.current?.getBoundingClientRect() ?? null
      await addItem(product, { color: product.colors?.[0], quantity: 1, sourceRect })
      pulseAddButton(addBtnRef.current)
    } catch (err) {
      setError(err.message || 'Unable to add item.')
    } finally {
      setAdding(false)
    }
  }

  return (
    <article className={`product-card ${soldOut ? 'is-sold' : ''}`}>
      <Link to={productHref} className={`product-media ${soldOut ? 'is-sold' : ''}`}>
        <img src={product.image} alt={product.name} loading="lazy" />
        {product.badge && !soldOut ? <span className="product-badge">{product.badge}</span> : null}
        {soldOut ? (
          <span className="sold-stamp" aria-label="Sold out">
            Sold
          </span>
        ) : null}
      </Link>

      <div className="product-meta">
        <span className="product-category">{product.category}</span>
        <Link to={productHref}>
          <h3>{product.name}</h3>
        </Link>
        <CompactRating rating={product.rating} reviews={product.reviews} />
        <div className="product-price-row">
          <span className="price">{formatPrice(product.price)}</span>
          {product.compareAt ? (
            <span className="price-compare">{formatPrice(product.compareAt)}</span>
          ) : null}
        </div>
      </div>

      {inCart && !soldOut ? (
        <p className="added-to-cart product-card-in-cart" role="status">
          <svg viewBox="0 0 24 24" aria-hidden="true">
            <circle cx="12" cy="12" r="10" />
            <path d="M8 12.5l2.5 2.5L16 9.5" />
          </svg>
          Added to cart
        </p>
      ) : null}
      <div className="product-actions">
        <Link to={productHref} className="btn btn-secondary">
          View
        </Link>
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
            disabled={adding}
          >
            {adding ? 'Adding…' : 'Add to cart'}
          </button>
        )}
      </div>
      {error ? <p className="product-card-error">{error}</p> : null}
    </article>
  )
}
