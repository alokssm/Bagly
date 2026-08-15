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

  const itemCount = items.reduce((sum, item) => sum + item.quantity, 0)

  return (
    <section className="section section--commerce">
      <div className="container">
        <div className="page-hero page-hero--commerce">
          <span className="eyebrow">Cart</span>
          <h1>Your selection</h1>
          <p>
            {itemCount} {itemCount === 1 ? 'item' : 'items'} ready for checkout
          </p>
        </div>

        {error ? (
          <ApiErrorState message={error} onRetry={retryBootstrap} compact className="cart-inline-error" />
        ) : null}

        <div className="cart-layout">
          <div className="cart-list">
            {items.map((item) => (
              <div className="cart-item" key={`${item.id}-${item.color}`}>
                <Link to={`/product/${item.id}`} className="cart-item__media">
                  <img src={item.image} alt={item.name} />
                </Link>
                <div className="cart-item__body">
                  <div className="cart-item__header">
                    <h3>
                      <Link to={`/product/${item.id}`}>{item.name}</Link>
                    </h3>
                    <p className="cart-item-meta">{item.color}</p>
                  </div>
                  <div className="cart-item-actions">
                    <div className="qty-control qty-control--compact">
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
                <div className="cart-item-price">
                  <span className="cart-item-price__line">{formatPrice(item.price * item.quantity)}</span>
                  {item.quantity > 1 ? (
                    <span className="cart-item-price__unit">{formatPrice(item.price)} each</span>
                  ) : null}
                </div>
              </div>
            ))}
          </div>

          <aside className="cart-summary">
            <h2>Order summary</h2>
            <div className="summary-rows">
              <div className="summary-row">
                <span>Subtotal</span>
                <span className="summary-row__value">{formatPrice(subtotal)}</span>
              </div>
              <div className="summary-row">
                <span>Shipping</span>
                <span className="summary-row__value">
                  {shipping === 0 ? 'Free' : formatPrice(shipping)}
                </span>
              </div>
              <div className="summary-row total">
                <span>Total</span>
                <span className="summary-row__value">{formatPrice(total)}</span>
              </div>
            </div>
            <div className="cart-summary__actions">
              <Link to="/checkout" className="btn btn-brass btn-commerce btn-block">
                Checkout
              </Link>
              <Link to="/shop" className="btn btn-ghost btn-block cart-summary__keep-shopping">
                Keep shopping
              </Link>
            </div>
          </aside>
        </div>
      </div>
    </section>
  )
}
