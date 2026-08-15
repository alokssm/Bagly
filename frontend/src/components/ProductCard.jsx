import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { formatPrice } from '../utils/format'
import { useCart } from '../context/CartContext'
import { pulseAddButton } from '../utils/cartAnim'
import { CompactRating } from './ProductReviews'

export default function ProductCard({ product }) {
  const { addItem, items } = useCart()
  const [adding, setAdding] = useState(false)
  const [error, setError] = useState('')

  const soldOut = product.inStock === false
  const productHref = `/product/${product.slug || product.id}`
  const inCart = useMemo(
    () => items.some((item) => item.id === product.id),
    [items, product.id],
  )

  const handleAdd = async (event) => {
    event.preventDefault()
    event.stopPropagation()
    const btn = event.currentTarget
    setAdding(true)
    setError('')
    try {
      const sourceRect = btn.getBoundingClientRect()
      await addItem(product, { color: product.colors?.[0], quantity: 1, sourceRect })
      pulseAddButton(btn)
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
        <div className="product-meta-row">
          <div className="product-price-row">
            <span className="price">{formatPrice(product.price)}</span>
            {product.compareAt ? (
              <span className="price-compare">{formatPrice(product.compareAt)}</span>
            ) : null}
          </div>
          <CompactRating rating={product.rating} reviews={product.reviews} />
        </div>
      </div>

      <div className="product-actions">
        {inCart && !soldOut ? (
          <p className="added-to-cart product-card-in-cart" role="status">
            <svg viewBox="0 0 24 24" aria-hidden="true">
              <circle cx="12" cy="12" r="10" />
              <path d="M8 12.5l2.5 2.5L16 9.5" />
            </svg>
            <span>Added to cart</span>
          </p>
        ) : null}
        {soldOut ? (
          <button type="button" className="btn btn-primary btn-add-cart btn-block" disabled>
            Sold out
          </button>
        ) : (
          <button
            type="button"
            className="btn btn-primary btn-add-cart btn-block"
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
