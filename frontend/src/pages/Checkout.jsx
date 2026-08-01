import { useEffect, useState } from 'react'
import { Link, Navigate, useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import { formatPrice } from '../utils/format'
import { buildCreateOrderPayload, buildShippingAddressPayload } from '../utils/payloads'
import { openRazorpayCheckout } from '../utils/razorpay'
import { useCart } from '../context/CartContext'
import { useCustomerAuth } from '../context/CustomerAuthContext'

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
  const { user, isAuthenticated, loading: authLoading } = useCustomerAuth()
  const navigate = useNavigate()
  const [form, setForm] = useState(initialForm)
  const [placed, setPlaced] = useState(null)
  const [submitting, setSubmitting] = useState(false)
  const [confirmingOrder, setConfirmingOrder] = useState(false)
  const [error, setError] = useState('')
  const [razorpayConfig, setRazorpayConfig] = useState(null)

  const [addresses, setAddresses] = useState([])
  const [addressesLoading, setAddressesLoading] = useState(false)
  const [selectedAddressId, setSelectedAddressId] = useState(null)
  const [saveAddress, setSaveAddress] = useState(false)
  const [addressLabel, setAddressLabel] = useState('')
  const [setAsDefault, setSetAsDefault] = useState(false)

  const isIndia = form.country === 'India'

  useEffect(() => {
    let cancelled = false
    api
      .getRazorpayConfig()
      .then((cfg) => {
        if (!cancelled) setRazorpayConfig(cfg)
      })
      .catch(() => {
        if (!cancelled) setRazorpayConfig({ enabled: false, currency: 'INR' })
      })
    return () => {
      cancelled = true
    }
  }, [])

  useEffect(() => {
    if (!confirmingOrder) return undefined

    const onBeforeUnload = (event) => {
      event.preventDefault()
      event.returnValue = ''
    }

    const blockBack = () => {
      window.history.pushState(null, '', window.location.href)
    }

    window.history.pushState(null, '', window.location.href)
    window.addEventListener('beforeunload', onBeforeUnload)
    window.addEventListener('popstate', blockBack)

    return () => {
      window.removeEventListener('beforeunload', onBeforeUnload)
      window.removeEventListener('popstate', blockBack)
    }
  }, [confirmingOrder])

  useEffect(() => {
    if (!isAuthenticated) {
      setAddresses([])
      return
    }

    let cancelled = false
    setAddressesLoading(true)
    api
      .getShippingAddresses()
      .then((list) => {
        if (cancelled) return
        setAddresses(list || [])
        const preferred = (list || []).find((a) => a.isDefault) || (list || [])[0]
        if (preferred) {
          applyAddress(preferred)
        }
      })
      .catch(() => {
        if (!cancelled) setAddresses([])
      })
      .finally(() => {
        if (!cancelled) setAddressesLoading(false)
      })
    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isAuthenticated])

  const applyAddress = (addr) => {
    setSelectedAddressId(addr.id)
    setForm({
      email: addr.email || user?.email || '',
      firstName: addr.firstName || '',
      lastName: addr.lastName || '',
      address: addr.address || '',
      city: addr.city || '',
      state: addr.state || '',
      zip: addr.zip || '',
      country: addr.country || 'India',
    })
  }

  if (authLoading) {
    return (
      <div className="container empty-state">
        <h2>Loading…</h2>
      </div>
    )
  }

  if (!isAuthenticated) {
    return (
      <Navigate
        to="/login"
        replace
        state={{ from: '/checkout', message: 'Sign in to continue to checkout.' }}
      />
    )
  }

  if (loading) {
    return (
      <div className="container empty-state">
        <h2>Loading checkout…</h2>
      </div>
    )
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
              {placed.email ? ` and a confirmation email will go to ${placed.email}` : ''}.
            </p>
            <p style={{ marginTop: '0.5rem' }}>
              Total charged:{' '}
              {formatPrice(
                placed.paymentProvider === 'Razorpay' && placed.amountInr != null
                  ? placed.amountInr
                  : placed.total,
              )}
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

  if (items.length === 0) {
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
    setSelectedAddressId(null)
    setForm((prev) => ({ ...prev, [name]: value }))
  }

  const selectSavedAddress = (addr) => {
    if (selectedAddressId === addr.id) return
    applyAddress(addr)
  }

  const finishSuccess = async (order) => {
    setPlaced(order)
    try {
      await clearCart()
    } catch {
      await refreshCart().catch(() => {})
    }
  }

  const saveAddressIfRequested = async () => {
    if (!isAuthenticated || !saveAddress) return
    try {
      await api.createShippingAddress(
        buildShippingAddressPayload({ ...form, label: addressLabel, isDefault: setAsDefault }),
      )
    } catch {
      // Non-critical: don't block order placement if saving the address fails.
    }
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

      await saveAddressIfRequested()

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
            currency: session.currency || 'INR',
            name: 'Bagly',
            description: session.description || `Order ${session.orderNumber}`,
            order_id: session.razorpayOrderId,
            prefill: {
              name: session.customerName,
              email: session.customerEmail,
              contact: '9999999999',
            },
            notes: {
              bagly_order_id: session.orderId,
              bagly_order_number: session.orderNumber,
              customer_country: 'India',
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

        setConfirmingOrder(true)
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

      setConfirmingOrder(true)
      const order = await api.createOrder(payload)
      await finishSuccess(order)
    } catch (err) {
      setConfirmingOrder(false)
      setError(err.message || 'Unable to place order. Is the API running?')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <section className="section" style={{ paddingTop: 0 }}>
      <div className="container">
        <div className="page-hero">
          <span className="eyebrow">Checkout</span>
          <h1>Complete your order</h1>
        </div>

        <form
          className={`checkout-layout${confirmingOrder ? ' checkout-layout--blocked' : ''}`}
          onSubmit={onSubmit}
        >
          <div className="form-card">
            <h2>Shipping details</h2>
            {error ? <p style={{ color: 'var(--danger)', marginBottom: '1rem' }}>{error}</p> : null}

            {isAuthenticated ? (
              <div className="saved-addresses">
                <span className="saved-addresses__label">Saved addresses</span>
                {addressesLoading ? (
                  <p className="saved-addresses__hint">Loading your addresses…</p>
                ) : addresses.length === 0 ? (
                  <p className="saved-addresses__hint">
                    You don't have any saved addresses yet. Fill the form below and check "Save
                    this address" to add one.
                  </p>
                ) : (
                  <div className="saved-addresses__list">
                    {addresses.map((addr) => (
                      <button
                        type="button"
                        key={addr.id}
                        className={`saved-address-card${selectedAddressId === addr.id ? ' active' : ''}`}
                        onClick={() => selectSavedAddress(addr)}
                      >
                        <span className="saved-address-card__top">
                          <strong>{addr.label || `${addr.firstName} ${addr.lastName}`}</strong>
                          {addr.isDefault ? (
                            <span className="saved-address-card__badge">Default</span>
                          ) : null}
                        </span>
                        <span className="saved-address-card__body">
                          {addr.address}, {addr.city}, {addr.state} {addr.zip}, {addr.country}
                        </span>
                      </button>
                    ))}
                  </div>
                )}
              </div>
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

            {isAuthenticated ? (
              <div className="save-address-block">
                <label className="save-address-check">
                  <input
                    type="checkbox"
                    checked={saveAddress}
                    onChange={(e) => setSaveAddress(e.target.checked)}
                  />
                  Save this address for next time
                </label>
                {saveAddress ? (
                  <div className="save-address-options">
                    <div className="form-field">
                      <label htmlFor="addressLabel">Label (optional)</label>
                      <input
                        id="addressLabel"
                        name="addressLabel"
                        placeholder="Home, Work…"
                        value={addressLabel}
                        onChange={(e) => setAddressLabel(e.target.value)}
                      />
                    </div>
                    <label className="save-address-check save-address-check--small">
                      <input
                        type="checkbox"
                        checked={setAsDefault}
                        onChange={(e) => setSetAsDefault(e.target.checked)}
                      />
                      Set as default address
                    </label>
                  </div>
                ) : null}
              </div>
            ) : null}
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
                  <span>{formatPrice(item.price * item.quantity)}</span>
                </div>
              ))}
            </div>
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

        {confirmingOrder ? (
          <div
            className="checkout-confirm-overlay"
            role="alertdialog"
            aria-live="assertive"
            aria-busy="true"
            aria-label={isIndia ? 'Confirming your payment' : 'Confirming your order'}
          >
            <div className="checkout-confirm-overlay__panel">
              <div className="checkout-confirm-overlay__spinner" aria-hidden="true" />
              <h2>{isIndia ? 'Confirming your payment…' : 'Confirming your order…'}</h2>
              <p>Please don&apos;t refresh or go back.</p>
            </div>
          </div>
        ) : null}
      </div>
    </section>
  )
}
