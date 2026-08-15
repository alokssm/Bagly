import { useEffect, useRef, useState } from 'react'
import { Link, Navigate, useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import { formatPrice } from '../utils/format'
import { buildCreateOrderPayload, buildShippingAddressPayload } from '../utils/payloads'
import { openRazorpayCheckout } from '../utils/razorpay'
import LoadingState from '../components/LoadingState'
import ApiErrorState from '../components/ApiErrorState'
import { useCart } from '../context/CartContext'
import { useCustomerAuth } from '../context/CustomerAuthContext'

const initialForm = {
  email: '',
  firstName: '',
  lastName: '',
  phone: '',
  address: '',
  city: '',
  state: '',
  zip: '',
  country: 'India',
}

export default function Checkout() {
  const { cartId, items, subtotal, shipping, total, clearCart, loading, refreshCart, loadFailed, retryBootstrap } = useCart()
  const { user, isAuthenticated, loading: authLoading } = useCustomerAuth()
  const navigate = useNavigate()
  const [form, setForm] = useState(initialForm)
  const [step, setStep] = useState('address')
  const [paymentMethod, setPaymentMethod] = useState('')
  const [placed, setPlaced] = useState(null)
  const [submitting, setSubmitting] = useState(false)
  const [confirmingOrder, setConfirmingOrder] = useState(false)
  const [error, setError] = useState('')
  const errorRef = useRef(null)
  const formRef = useRef(null)
  const [razorpayConfig, setRazorpayConfig] = useState(null)

  const [addresses, setAddresses] = useState([])
  const [addressesLoading, setAddressesLoading] = useState(false)
  const [selectedAddressId, setSelectedAddressId] = useState(null)
  const [saveAddress, setSaveAddress] = useState(false)
  const [addressLabel, setAddressLabel] = useState('')
  const [setAsDefault, setSetAsDefault] = useState(false)

  const isIndia = form.country === 'India'
  const razorpayAvailable = isIndia && !!razorpayConfig?.enabled
  const codOnly = !razorpayAvailable

  useEffect(() => {
    if (step === 'payment' && codOnly && paymentMethod !== 'COD') {
      setPaymentMethod('COD')
    }
  }, [step, codOnly, paymentMethod])

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
    window.scrollTo({ top: 0, behavior: 'smooth' })
  }, [step])

  useEffect(() => {
    if (!error) return
    window.scrollTo({ top: 0, behavior: 'smooth' })
    errorRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  }, [error])

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
        } else if (user?.email) {
          // No saved address yet — at least prefill the account email so it's not left blank.
          setForm((prev) => ({ ...prev, email: prev.email || user.email }))
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
      phone: addr.phone || '',
      address: addr.address || '',
      city: addr.city || '',
      state: addr.state || '',
      zip: addr.zip || '',
      country: addr.country || 'India',
    })
  }

  if (authLoading) {
    return (
      <div className="container">
        <LoadingState message="Loading checkout…" />
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
      <div className="container">
        <LoadingState message="Loading checkout…" />
      </div>
    )
  }

  if (loadFailed) {
    return (
      <div className="container">
        <ApiErrorState
          title="Couldn't load checkout"
          message="We're having trouble reaching Bagly right now. Please try again in a moment."
          onRetry={retryBootstrap}
        >
          <Link to="/cart" className="btn btn-secondary">
            Back to cart
          </Link>
        </ApiErrorState>
      </div>
    )
  }

  if (placed) {
    return (
      <section className="section section--commerce">
        <div className="container checkout-success">
          <div className="success-banner">
            <span className="eyebrow">Order placed</span>
            <h2>Order confirmed</h2>
            <p>
              Thanks{placed.firstName ? `, ${placed.firstName}` : ''}. Order{' '}
              <strong>{placed.orderNumber}</strong> is confirmed
              {placed.email ? ` and a confirmation email will go to ${placed.email}` : ''}.
            </p>
            <p className="success-banner__total">
              Total charged:{' '}
              <strong>
                {formatPrice(
                  placed.paymentProvider === 'Razorpay' && placed.amountInr != null
                    ? placed.amountInr
                    : placed.total,
                )}
              </strong>
            </p>
            {placed.razorpayPaymentId ? (
              <p className="success-banner__meta">
                Razorpay payment ID: {placed.razorpayPaymentId}
              </p>
            ) : null}
            {placed.paymentProvider === 'COD' ? (
              <p className="success-banner__meta">
                Please keep {formatPrice(placed.total)} ready in cash for delivery.
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
    let next = value
    if (name === 'zip' && form.country === 'India') {
      next = String(value || '')
        .replace(/\D/g, '')
        .slice(0, 6)
    }
    setForm((prev) => ({ ...prev, [name]: next }))
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

  const showCheckoutError = (message) => {
    setConfirmingOrder(false)
    setError(message)
  }

  const backToAddress = () => {
    setError('')
    setStep('address')
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
    setError('')

    if (step === 'address') {
      if (isIndia) {
        const pin = String(form.zip || '').replace(/\D/g, '')
        if (!/^[1-9]\d{5}$/.test(pin)) {
          setError('Enter a valid 6-digit Indian PIN code (e.g. 110001).')
          return
        }
        if (pin !== form.zip) {
          setForm((prev) => ({ ...prev, zip: pin }))
        }
      }
      setStep('payment')
      return
    }

    if (!paymentMethod) {
      setError('Please choose a payment method to continue.')
      return
    }

    setSubmitting(true)

    try {
      const payload = buildCreateOrderPayload({
        ...form,
        cartId,
        items: items.map((item) => ({
          productId: item.id,
          color: item.color,
          quantity: item.quantity,
        })),
        paymentMethod,
      })

      await saveAddressIfRequested()

      if (paymentMethod === 'RAZORPAY') {
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
              contact: String(form.phone || '').replace(/\D/g, '').slice(-10) || undefined,
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
      showCheckoutError(err.message || 'Unable to place order. Please try again.')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <section className="section section--commerce">
      <div className="container">
        <div className="page-hero page-hero--commerce">
          <span className="eyebrow">Checkout</span>
          <h1>Complete your order</h1>
          <ol className="checkout-steps" aria-label="Checkout progress">
            <li
              className={step === 'address' ? 'is-active' : 'is-complete'}
              aria-current={step === 'address' ? 'step' : undefined}
            >
              <span className="checkout-steps__index">1</span>
              <span className="checkout-steps__label">Shipping</span>
            </li>
            <li
              className={step === 'payment' ? 'is-active' : undefined}
              aria-current={step === 'payment' ? 'step' : undefined}
            >
              <span className="checkout-steps__index">2</span>
              <span className="checkout-steps__label">Payment</span>
            </li>
          </ol>
        </div>

        <form
          ref={formRef}
          className={`checkout-layout${confirmingOrder ? ' checkout-layout--blocked' : ''}`}
          onSubmit={onSubmit}
        >
          <div className="form-card">
            <h2>{step === 'address' ? 'Shipping details' : 'Payment method'}</h2>
            {error ? (
              <ApiErrorState
                message={error}
                onRetry={() => formRef.current?.requestSubmit()}
                compact
                className="checkout-form-error"
              />
            ) : null}

            {step === 'address' ? (
              <>
                {isAuthenticated ? (
                  <div className="saved-addresses">
                    <span className="saved-addresses__label">Saved addresses</span>
                    {addressesLoading ? (
                      <LoadingState message="Loading your addresses…" compact />
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
                    <label htmlFor="phone">Phone</label>
                    <input
                      id="phone"
                      name="phone"
                      type="tel"
                      required={isIndia}
                      value={form.phone}
                      onChange={onChange}
                      placeholder="10-digit mobile"
                      inputMode="tel"
                      autoComplete="tel"
                      pattern={isIndia ? '[6-9][0-9]{9}' : undefined}
                      title={isIndia ? 'Enter a 10-digit Indian mobile number' : undefined}
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
                    <input
                      id="zip"
                      name="zip"
                      required
                      value={form.zip}
                      onChange={onChange}
                      inputMode={isIndia ? 'numeric' : undefined}
                      autoComplete="postal-code"
                      maxLength={isIndia ? 6 : undefined}
                      pattern={isIndia ? '[1-9][0-9]{5}' : undefined}
                      title={isIndia ? 'Enter a 6-digit Indian PIN code' : undefined}
                      placeholder={isIndia ? '6-digit PIN' : undefined}
                    />
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
              </>
            ) : (
              <>
                <div className="checkout-address-summary">
                  <div className="checkout-address-summary__body">
                    <strong>
                      {form.firstName} {form.lastName}
                    </strong>
                    <span>
                      {form.address}, {form.city}, {form.state} {form.zip}, {form.country}
                    </span>
                    <span>{form.email}</span>
                    {form.phone ? <span>{form.phone}</span> : null}
                  </div>
                  <button type="button" className="btn btn-secondary" onClick={backToAddress}>
                    Change
                  </button>
                </div>

                <div className="payment-method-list">
                  <label
                    className={`payment-method-card${paymentMethod === 'COD' ? ' active' : ''}`}
                  >
                    <input
                      type="radio"
                      name="paymentMethod"
                      value="COD"
                      checked={paymentMethod === 'COD'}
                      onChange={() => setPaymentMethod('COD')}
                    />
                    <span className="payment-method-card__body">
                      <span className="payment-method-card__title">Cash on delivery</span>
                      <span className="payment-method-card__desc">
                        Pay in cash when your order is delivered.
                      </span>
                    </span>
                  </label>

                  {razorpayAvailable ? (
                    <label
                      className={`payment-method-card${paymentMethod === 'RAZORPAY' ? ' active' : ''}`}
                    >
                      <input
                        type="radio"
                        name="paymentMethod"
                        value="RAZORPAY"
                        checked={paymentMethod === 'RAZORPAY'}
                        onChange={() => setPaymentMethod('RAZORPAY')}
                      />
                      <span className="payment-method-card__body">
                        <span className="payment-method-card__title">Pay now</span>
                        <span className="payment-method-card__desc">
                          Pay securely online via UPI, cards or netbanking (Razorpay).
                        </span>
                      </span>
                    </label>
                  ) : null}
                </div>

                <button
                  type="button"
                  className="btn btn-secondary checkout-back-btn"
                  onClick={backToAddress}
                >
                  ← Back to address
                </button>
              </>
            )}
          </div>

          <aside className="cart-summary">
            <h2>Your bag</h2>
            <div className="order-lines">
              {items.map((item) => (
                <div className="order-line" key={`${item.id}-${item.color}`}>
                  <div className="order-line__info">
                    <span className="order-line__name">
                      {item.name}
                      <span className="order-line__qty"> × {item.quantity}</span>
                    </span>
                    <span className="order-line__meta">{item.color}</span>
                  </div>
                  <span className="order-line__price">{formatPrice(item.price * item.quantity)}</span>
                </div>
              ))}
            </div>
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
            <button
              type="submit"
              className="btn btn-brass btn-commerce btn-block cart-summary__submit"
              disabled={submitting || (step === 'payment' && !paymentMethod)}
            >
              {step === 'address'
                ? 'Proceed to pay'
                : submitting
                  ? paymentMethod === 'RAZORPAY'
                    ? 'Opening Razorpay…'
                    : 'Placing order…'
                  : 'Continue'}
            </button>
          </aside>
        </form>

        {confirmingOrder ? (
          <div
            className="checkout-confirm-overlay"
            role="alertdialog"
            aria-live="assertive"
            aria-busy="true"
            aria-label={paymentMethod === 'RAZORPAY' ? 'Confirming your payment' : 'Confirming your order'}
          >
            <div className="checkout-confirm-overlay__panel">
              <div className="checkout-confirm-overlay__spinner" aria-hidden="true" />
              <h2>{paymentMethod === 'RAZORPAY' ? 'Confirming your payment…' : 'Confirming your order…'}</h2>
              <p>Please don&apos;t refresh or go back.</p>
            </div>
          </div>
        ) : null}
      </div>
    </section>
  )
}
