import { Link } from 'react-router-dom'
import { formatPrice } from '../utils/format'
import { useCart } from '../context/CartContext'
import LoadingState from '../components/LoadingState'
import ApiErrorState from '../components/ApiErrorState'

export default function Cart() {
  const {
    items,
    subtotal,
    shipping,
    total,
    loading,
    busy,
    error,
    loadFailed,
    updateQuantity,
    removeItem,
    retryBootstrap,
  } = useCart()

  if (loading) {
    return (
      <div className="container">
        <LoadingState message="Loading your cart…" />
      </div>
    )
  }

  if (loadFailed) {
    return (
      <div className="container">
        <ApiErrorState
          title="Couldn't load your cart"
          message={error}
          onRetry={retryBootstrap}
        >
          <Link to="/shop" className="btn btn-secondary">
            Continue shopping
          </Link>
        </ApiErrorState>
      </div>
    )
  }

  if (items.length === 0) {
    return (
      <div className="container empty-state">
        <h2>Your cart is empty</h2>
        <p>Find a bag that fits your day and add it here.</p>
        <Link to="/shop" className="btn btn-primary">
          Continue shopping
        </Link>
      </div>
    )
  }

  return (
    <section className="section" style={{ paddingTop: 0 }}>
      <div className="container">
        <div className="page-hero">
          <span className="eyebrow">Cart</span>
          <h1>Your selection</h1>
        </div>

        {error ? (
          <ApiErrorState message={error} onRetry={retryBootstrap} compact className="cart-inline-error" />
        ) : null}

        <div className="cart-layout">
          <div className="cart-list">
            {items.map((item) => (
              <div className="cart-item" key={`${item.id}-${item.color}`}>
                <img src={item.image} alt={item.name} />
                <div>
                  <h3>
                    <Link to={`/product/${item.id}`}>{item.name}</Link>
                  </h3>
                  <p className="cart-item-meta">Color: {item.color}</p>
                  <div className="cart-item-actions">
                    <div className="qty-control">
                      <button
                        type="button"
                        disabled={busy}
                        onClick={() => updateQuantity(item.id, item.color, item.quantity - 1)}
                        aria-label="Decrease"
                      >
                        −
                      </button>
                      <span>{item.quantity}</span>
                      <button
                        type="button"
                        disabled={busy}
                        onClick={() => updateQuantity(item.id, item.color, item.quantity + 1)}
                        aria-label="Increase"
                      >
                        +
                      </button>
                    </div>
                    <button
                      type="button"
                      className="remove-btn"
                      disabled={busy}
                      onClick={() => removeItem(item.id, item.color)}
                    >
                      Remove
                    </button>
                  </div>
                </div>
                <div className="cart-item-price price">
                  {formatPrice(item.price * item.quantity)}
                </div>
              </div>
            ))}
          </div>

          <aside className="cart-summary">
            <h2>Order summary</h2>
            <div className="summary-row">
              <span>Subtotal</span>
              <span>{formatPrice(subtotal)}</span>
            </div>
            <div className="summary-row">
              <span>Shipping</span>
              <span>{shipping === 0 ? 'Free' : formatPrice(shipping)}</span>
            </div>
            <div className="summary-row total">
              <span>Total</span>
              <span>{formatPrice(total)}</span>
            </div>
            <Link to="/checkout" className="btn btn-brass btn-block">
              Checkout
            </Link>
            <Link to="/shop" className="btn btn-ghost btn-block" style={{ color: '#eef3ef' }}>
              Keep shopping
            </Link>
          </aside>
        </div>
      </div>
    </section>
  )
}
