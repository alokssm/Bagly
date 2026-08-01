import { useState } from 'react'
import { Link } from 'react-router-dom'
import { formatPrice } from '../utils/format'
import { useCart } from '../context/CartContext'

export default function ProductCard({ product }) {
  const { addItem } = useCart()
  const [adding, setAdding] = useState(false)
  const [error, setError] = useState('')

  const soldOut = product.inStock === false

  const handleAdd = async () => {
    setAdding(true)
    setError('')
    try {
      await addItem(product, { color: product.colors?.[0], quantity: 1 })
    } catch (err) {
      setError(err.message || 'Unable to add item.')
    } finally {
      setAdding(false)
    }
  }

  return (
    <article className={`product-card ${soldOut ? 'is-sold' : ''}`}>
      <Link to={`/product/${product.id}`} className={`product-media ${soldOut ? 'is-sold' : ''}`}>
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
        <Link to={`/product/${product.id}`}>
          <h3>{product.name}</h3>
        </Link>
        <div className="product-price-row">
          <span className="price">{formatPrice(product.price)}</span>
          {product.compareAt ? (
            <span className="price-compare">{formatPrice(product.compareAt)}</span>
          ) : null}
        </div>
      </div>

      <div className="product-actions">
        <Link to={`/product/${product.id}`} className="btn btn-secondary">
          View
        </Link>
        {soldOut ? (
          <button type="button" className="btn btn-primary" disabled>
            Sold out
          </button>
        ) : (
          <button type="button" className="btn btn-primary" onClick={handleAdd} disabled={adding}>
            {adding ? 'Adding…' : 'Add to cart'}
          </button>
        )}
      </div>
      {error ? <p className="product-card-error">{error}</p> : null}
    </article>
  )
}
