import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import { formatPrice } from '../utils/format'
import { buildCreateOrderPayload } from '../utils/payloads'
import { openRazorpayCheckout } from '../utils/razorpay'
import { useCart } from '../context/CartContext'

const initialForm = {
  email: '',
  firstName: '',
  lastName: '',
  address: '',
  city: '',
  state: '',
  zip: '',
  country: 'India',
}

export default function Checkout() {
  const { cartId, items, subtotal, shipping, total, clearCart, loading, refreshCart } = useCart()
  const navigate = useNavigate()
  const [form, setForm] = useState(initialForm)
  const [placed, setPlaced] = useState(null)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState('')
  const [razorpayConfig, setRazorpayConfig] = useState(null)

  const isIndia = form.country === 'India'
  const displayCurrency = isIndia ? 'INR' : 'USD'
  const displayTotal = useMemo(() => {
    if (!isIndia) return total
    const rate = razorpayConfig?.usdToInrRate || 83
    return Math.round(total * rate * 100) / 100
  }, [isIndia, total, razorpayConfig])

  useEffect(() => {
    let cancelled = false
    api
      .getRazorpayConfig()
      .then((cfg) => {
        if (!cancelled) setRazorpayConfig(cfg)
      })
      .catch(() => {
        if (!cancelled) setRazorpayConfig({ enabled: false, usdToInrRate: 83, currency: 'INR' })
      })
    return () => {
      cancelled = true
    }
  }, [])

  if (loading) {
    return (
      <div className="container empty-state">
        <h2>Loading checkout…</h2>
      </div>
    )
  }

  if (items.length === 0 && !placed) {
    return (
      <div className="container empty-state">
        <h2>Nothing to checkout</h2>
        <p>Add a bag to your cart first.</p>
        <Link to="/shop" className="btn btn-primary">
          Browse bags
        </Link>
      </div>
    )
  }

  const onChange = (e) => {
    const { name, value } = e.target
    setForm((prev) => ({ ...prev, [name]: value }))
  }

  const finishSuccess = async (order) => {
    try {
      await clearCart()
    } catch {
      await refreshCart().catch(() => {})
    }
    setPlaced(order)
  }

  const onSubmit = async (e) => {
    e.preventDefault()
    setSubmitting(true)
    setError('')

    try {
      const payload = buildCreateOrderPayload({
        ...form,
        cartId,
        items: items.map((item) => ({
          productId: item.id,
          color: item.color,
          quantity: item.quantity,
        })),
      })

      if (isIndia) {
        if (!razorpayConfig?.enabled) {
          throw new Error(
            'Razorpay is not configured yet. Add your test KeyId and KeySecret in backend appsettings.json.',
          )
        }

        const session = await api.initiateRazorpayPayment(payload)

        let paymentResponse
        try {
          paymentResponse = await openRazorpayCheckout({
            key: session.keyId,
            amount: session.amountPaise,
            currency: session.currency,
            name: 'Bagly',
            description: session.description || `Order ${session.orderNumber}`,
            order_id: session.razorpayOrderId,
            prefill: {
              name: session.customerName,
              email: session.customerEmail,
            },
            notes: {
              bagly_order_id: session.orderId,
              bagly_order_number: session.orderNumber,
            },
            theme: { color: '#1b3d2f' },
          })
        } catch (payErr) {
          const rzErr = payErr.razorpayError
          await api
            .reportRazorpayFailure({
              orderId: session.orderId,
              razorpayOrderId: session.razorpayOrderId,
              code: rzErr?.code || 'CHECKOUT_DISMISSED',
              description: payErr.message || rzErr?.description || 'Payment cancelled',
            })
            .catch(() => {})
          throw payErr
        }

        const order = await api.verifyRazorpayPayment({
          orderId: session.orderId,
          razorpayOrderId: paymentResponse.razorpay_order_id,
          razorpayPaymentId: paymentResponse.razorpay_payment_id,
          razorpaySignature: paymentResponse.razorpay_signature,
          cartId: cartId || null,
        })

        await finishSuccess(order)
        return
      }

      const order = await api.createOrder(payload)
      await finishSuccess(order)
    } catch (err) {
      setError(err.message || 'Unable to place order. Is the API running?')
    } finally {
      setSubmitting(false)
    }
  }

  if (placed) {
    return (
      <section className="section">
        <div className="container" style={{ maxWidth: 640 }}>
          <div className="success-banner">
            <h2>Order confirmed</h2>
            <p>
              Thanks{placed.firstName ? `, ${placed.firstName}` : ''}. Order{' '}
              <strong>{placed.orderNumber}</strong> is confirmed
              {placed.email ? ` and details will go to ${placed.email}` : ''}.
            </p>
            <p style={{ marginTop: '0.5rem' }}>
              Total charged:{' '}
              {placed.paymentProvider === 'Razorpay' && placed.amountInr != null
                ? formatPrice(placed.amountInr, 'INR')
                : formatPrice(placed.total, 'USD')}
            </p>
            {placed.razorpayPaymentId ? (
              <p style={{ marginTop: '0.35rem', opacity: 0.8 }}>
                Razorpay payment ID: {placed.razorpayPaymentId}
              </p>
            ) : null}
          </div>
          <button type="button" className="btn btn-primary" onClick={() => navigate('/shop')}>
            Continue shopping
          </button>
        </div>
      </section>
    )
  }

  return (
    <section className="section" style={{ paddingTop: 0 }}>
      <div className="container">
        <div className="page-hero">
          <span className="eyebrow">Checkout</span>
          <h1>Complete your order</h1>
        </div>

        <form className="checkout-layout" onSubmit={onSubmit}>
          <div className="form-card">
            <h2>Shipping details</h2>
            {error ? <p style={{ color: 'var(--danger)', marginBottom: '1rem' }}>{error}</p> : null}
            {isIndia ? (
              <p className="checkout-pay-note">
                India selected — final payment will open Razorpay (UPI / cards / netbanking). Catalog
                prices convert to INR at ≈₹{razorpayConfig?.usdToInrRate || 83} per $1.
              </p>
            ) : null}
            <div className="form-grid">
              <div className="form-field full">
                <label htmlFor="email">Email</label>
                <input
                  id="email"
                  name="email"
                  type="email"
                  required
                  value={form.email}
                  onChange={onChange}
                  placeholder="you@example.com"
                />
              </div>
              <div className="form-field">
                <label htmlFor="firstName">First name</label>
                <input
                  id="firstName"
                  name="firstName"
                  required
                  value={form.firstName}
                  onChange={onChange}
                />
              </div>
              <div className="form-field">
                <label htmlFor="lastName">Last name</label>
                <input
                  id="lastName"
                  name="lastName"
                  required
                  value={form.lastName}
                  onChange={onChange}
                />
              </div>
              <div className="form-field full">
                <label htmlFor="address">Address</label>
                <input
                  id="address"
                  name="address"
                  required
                  value={form.address}
                  onChange={onChange}
                />
              </div>
              <div className="form-field">
                <label htmlFor="city">City</label>
                <input id="city" name="city" required value={form.city} onChange={onChange} />
              </div>
              <div className="form-field">
                <label htmlFor="state">State</label>
                <input id="state" name="state" required value={form.state} onChange={onChange} />
              </div>
              <div className="form-field">
                <label htmlFor="zip">{isIndia ? 'PIN code' : 'ZIP'}</label>
                <input id="zip" name="zip" required value={form.zip} onChange={onChange} />
              </div>
              <div className="form-field">
                <label htmlFor="country">Country</label>
                <select id="country" name="country" value={form.country} onChange={onChange}>
                  <option>India</option>
                  <option>United States</option>
                  <option>Canada</option>
                  <option>United Kingdom</option>
                </select>
              </div>
            </div>
          </div>

          <aside className="cart-summary">
            <h2>Your bag</h2>
            <div className="order-lines">
              {items.map((item) => (
                <div className="order-line" key={`${item.id}-${item.color}`}>
                  <span>
                    {item.name} × {item.quantity}
                    <br />
                    <small style={{ opacity: 0.75 }}>{item.color}</small>
                  </span>
                  <span>
                    {formatPrice(
                      isIndia
                        ? item.price * item.quantity * (razorpayConfig?.usdToInrRate || 83)
                        : item.price * item.quantity,
                      displayCurrency,
                    )}
                  </span>
                </div>
              ))}
            </div>
            <div className="summary-row">
              <span>Subtotal</span>
              <span>
                {formatPrice(
                  isIndia ? subtotal * (razorpayConfig?.usdToInrRate || 83) : subtotal,
                  displayCurrency,
                )}
              </span>
            </div>
            <div className="summary-row">
              <span>Shipping</span>
              <span>
                {shipping === 0
                  ? 'Free'
                  : formatPrice(
                      isIndia ? shipping * (razorpayConfig?.usdToInrRate || 83) : shipping,
                      displayCurrency,
                    )}
              </span>
            </div>
            <div className="summary-row total">
              <span>Total</span>
              <span>{formatPrice(displayTotal, displayCurrency)}</span>
            </div>
            <button type="submit" className="btn btn-brass btn-block" disabled={submitting}>
              {submitting
                ? isIndia
                  ? 'Opening Razorpay…'
                  : 'Placing order…'
                : isIndia
                  ? 'Pay with Razorpay'
                  : 'Place order'}
            </button>
          </aside>
        </form>
      </div>
    </section>
  )
}
