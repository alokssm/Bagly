import { useState } from 'react'
import { Link } from 'react-router-dom'
import { formatPrice } from '../utils/format'
import { useCart } from '../context/CartContext'

export default function ProductCard({ product }) {
  const { addItem } = useCart()
  const [adding, setAdding] = useState(false)

  const handleAdd = async () => {
    setAdding(true)
    try {
      await addItem(product, { color: product.colors?.[0], quantity: 1 })
    } catch {
      // error handled in cart context
    } finally {
      setAdding(false)
    }
  }

  return (
    <article className="product-card">
      <Link to={`/product/${product.id}`} className="product-media">
        <img src={product.image} alt={product.name} loading="lazy" />
        {product.badge ? <span className="product-badge">{product.badge}</span> : null}
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
        <button type="button" className="btn btn-primary" onClick={handleAdd} disabled={adding}>
          {adding ? 'Adding…' : 'Add to cart'}
        </button>
      </div>
    </article>
  )
}
