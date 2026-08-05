import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import { formatPrice } from '../utils/format'
import LoadingState from '../components/LoadingState'
import ApiErrorState from '../components/ApiErrorState'

const STATUS_LABELS = {
  Confirmed: 'Confirmed',
  Processing: 'Processing',
  Shipped: 'Shipped',
  Delivered: 'Delivered',
  Cancelled: 'Cancelled',
}

function formatDate(value) {
  if (!value) return ''
  return new Date(value).toLocaleDateString('en-IN', {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
  })
}

function OrderCard({ order }) {
  const [open, setOpen] = useState(false)
  const itemCount = order.items.reduce((sum, item) => sum + item.quantity, 0)

  return (
    <div className="order-card">
      <button
        type="button"
        className="order-card__summary"
        onClick={() => setOpen((v) => !v)}
        aria-expanded={open}
      >
        <div className="order-card__main">
          <span className="order-card__number">{order.orderNumber}</span>
          <span className="order-card__meta">
            {formatDate(order.createdAt)} · {itemCount} item{itemCount === 1 ? '' : 's'}
          </span>
        </div>
        <div className="order-card__status">
          <span className={`order-status order-status--${order.status?.toLowerCase()}`}>
            {STATUS_LABELS[order.status] || order.status}
          </span>
          {order.paymentStatus ? (
            <span
              className={`order-status order-status--pay-${order.paymentStatus?.toLowerCase()}`}
            >
              {order.paymentStatus}
            </span>
          ) : null}
        </div>
        <div className="order-card__total">
          <span className="price">{formatPrice(order.total)}</span>
          <span className="order-card__chevron" aria-hidden="true">
            {open ? '−' : '+'}
          </span>
        </div>
      </button>

      {open ? (
        <div className="order-card__details">
          <div className="order-items">
            {order.items.map((item, idx) => (
              <div className="order-item-row" key={`${item.productId}-${item.color}-${idx}`}>
                {item.image ? (
                  <img src={item.image} alt={item.productName} />
                ) : (
                  <div className="order-item-row__placeholder" aria-hidden="true" />
                )}
                <div className="order-item-row__info">
                  <span className="order-item-row__name">{item.productName}</span>
                  <span className="order-item-row__meta">
                    {item.color ? `${item.color} · ` : ''}Qty {item.quantity}
                  </span>
                </div>
                <span className="order-item-row__price">
                  {formatPrice(item.unitPrice * item.quantity)}
                </span>
              </div>
            ))}
          </div>

          <div className="order-card__totals">
            <div className="summary-row">
              <span>Subtotal</span>
              <span>{formatPrice(order.subtotal)}</span>
            </div>
            <div className="summary-row">
              <span>Shipping</span>
              <span>{order.shipping === 0 ? 'Free' : formatPrice(order.shipping)}</span>
            </div>
            <div className="summary-row total">
              <span>Total</span>
              <span>{formatPrice(order.total)}</span>
            </div>
          </div>
        </div>
      ) : null}
    </div>
  )
}

export default function Orders() {
  const [orders, setOrders] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const load = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      const list = await api.getMyOrders()
      setOrders(list || [])
    } catch (err) {
      setError(err.message || 'Unable to load your orders.')
      setOrders([])
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    load()
  }, [load])

  if (loading) {
    return (
      <div className="container">
        <LoadingState message="Loading your orders…" />
      </div>
    )
  }

  if (error) {
    return (
      <div className="container">
        <ApiErrorState
          title="Couldn't load your orders"
          message={error}
          onRetry={load}
        >
          <Link to="/" className="btn btn-secondary">
            Back home
          </Link>
        </ApiErrorState>
      </div>
    )
  }

  if (orders.length === 0) {
    return (
      <div className="container empty-state">
        <h2>No orders yet</h2>
        <p>Once you place an order, it'll show up here.</p>
        <Link to="/shop" className="btn btn-primary">
          Start shopping
        </Link>
      </div>
    )
  }

  return (
    <section className="section" style={{ paddingTop: 0 }}>
      <div className="container">
        <div className="page-hero">
          <span className="eyebrow">Your account</span>
          <h1>My orders</h1>
        </div>

        <div className="orders-list">
          {orders.map((order) => (
            <OrderCard order={order} key={order.id} />
          ))}
        </div>
      </div>
    </section>
  )
}
