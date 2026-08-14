import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import SellerHubNav from '../components/SellerHubNav'
import { useSellerAuth } from '../context/SellerAuthContext'

const MAX_PICKUPS = 2

const emptyForm = {
  pickupLocation: '',
  name: '',
  email: '',
  phone: '',
  address: '',
  address2: '',
  city: '',
  state: '',
  country: 'India',
  pinCode: '',
  lat: '',
  long: '',
  gstin: '',
}

function digitsOnly(value) {
  return String(value || '').replace(/\D/g, '')
}

function validateForm(form) {
  const nickname = form.pickupLocation.trim()
  if (!nickname) return 'Pickup nickname is required.'
  if (nickname.length > 36) return 'Pickup nickname must be at most 36 characters.'
  if (!form.name.trim()) return 'Contact name is required.'
  if (!form.email.trim()) return 'Email is required.'
  const phone = digitsOnly(form.phone)
  if (!/^\d{10}$/.test(phone)) return 'Phone must be a 10-digit Indian mobile number.'
  if (!form.address.trim()) return 'Address is required.'
  if (form.address.trim().length > 80) return 'Address must be at most 80 characters.'
  if (form.address2.trim().length > 80) return 'Address line 2 must be at most 80 characters.'
  if (!form.city.trim()) return 'City is required.'
  if (!form.state.trim()) return 'State is required.'
  const pin = digitsOnly(form.pinCode)
  if (!/^\d{6}$/.test(pin)) return 'Pin code must be a 6-digit number.'
  return ''
}

export default function SellerPickups() {
  const { user, logout } = useSellerAuth()
  const approved = (user?.status || '').toLowerCase() === 'approved'
  const [items, setItems] = useState([])
  const [count, setCount] = useState(0)
  const [maxAllowed, setMaxAllowed] = useState(MAX_PICKUPS)
  const [form, setForm] = useState(emptyForm)
  const [loading, setLoading] = useState(approved)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [success, setSuccess] = useState('')

  const atMax = count >= maxAllowed

  const load = useCallback(async () => {
    if (!approved) {
      setLoading(false)
      return
    }
    setLoading(true)
    setError('')
    try {
      const [pickups, profile] = await Promise.all([
        api.sellerGetPickups(),
        api.sellerGetProfile().catch(() => null),
      ])
      const list = pickups?.items || []
      setItems(list)
      setCount(pickups?.count ?? list.length)
      setMaxAllowed(pickups?.maxAllowed ?? MAX_PICKUPS)

      if (profile) {
        setForm((prev) => ({
          ...prev,
          name: prev.name || profile.name || '',
          email: prev.email || profile.email || user?.email || '',
          phone: prev.phone || digitsOnly(profile.phone) || '',
          address: prev.address || profile.addressLine1 || '',
          address2: prev.address2 || profile.addressLine2 || '',
          city: prev.city || profile.city || '',
          state: prev.state || profile.state || '',
          pinCode: prev.pinCode || digitsOnly(profile.pincode) || '',
          gstin: prev.gstin || profile.gstin || '',
          country: prev.country || 'India',
        }))
      } else if (user?.email) {
        setForm((prev) => ({ ...prev, email: prev.email || user.email }))
      }
    } catch (err) {
      setError(err.message || 'Unable to load pickup locations.')
      setItems([])
      setCount(0)
    } finally {
      setLoading(false)
    }
  }, [approved, user?.email])

  useEffect(() => {
    load()
  }, [load])

  const onChange = (field) => (e) => {
    const value = e.target.value
    setForm((prev) => ({ ...prev, [field]: value }))
  }

  const onSubmit = async (e) => {
    e.preventDefault()
    if (!approved || atMax) return
    const validation = validateForm(form)
    if (validation) {
      setError(validation)
      return
    }
    setSaving(true)
    setError('')
    setSuccess('')
    try {
      await api.sellerCreatePickup({
        pickupLocation: form.pickupLocation.trim(),
        name: form.name.trim(),
        email: form.email.trim(),
        phone: digitsOnly(form.phone),
        address: form.address.trim(),
        address2: form.address2.trim() || null,
        city: form.city.trim(),
        state: form.state.trim(),
        country: (form.country || 'India').trim(),
        pinCode: digitsOnly(form.pinCode),
        lat: form.lat.trim() || null,
        long: form.long.trim() || null,
        gstin: form.gstin.trim() || null,
      })
      setSuccess('Pickup location created in Shiprocket and saved.')
      setForm((prev) => ({
        ...emptyForm,
        name: prev.name,
        email: prev.email,
        phone: prev.phone,
        address: prev.address,
        address2: prev.address2,
        city: prev.city,
        state: prev.state,
        country: prev.country || 'India',
        pinCode: prev.pinCode,
        gstin: prev.gstin,
      }))
      await load()
    } catch (err) {
      setError(err.message || 'Unable to create pickup location.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="seller-page">
      <div className="seller-shell seller-shell--wide">
        <header className="seller-head">
          <div>
            <p className="eyebrow">Seller hub</p>
            <h1>Pickup locations</h1>
            <p className="seller-lead">
              Add up to {maxAllowed} Shiprocket pickup addresses. Use the exact nickname on your products.
            </p>
          </div>
          <button type="button" className="btn btn-ghost" onClick={logout}>
            Sign out
          </button>
        </header>

        <SellerHubNav />

        {!approved ? (
          <div className="seller-status seller-status--pending" role="status">
            <strong>Awaiting admin approval</strong>
            <span>Pickup management is locked until your seller account is approved.</span>
          </div>
        ) : (
          <>
            <div className="seller-toolbar" style={{ justifyContent: 'space-between' }}>
              <p className="admin-muted" role="status">
                {count} of {maxAllowed} pickup locations used
              </p>
              <Link to="/seller/products" className="btn btn-ghost">
                Products
              </Link>
            </div>

            {loading ? <p className="admin-muted">Loading pickups…</p> : null}
            {error ? <p className="admin-error">{error}</p> : null}
            {success ? <p className="profile-success">{success}</p> : null}

            {!loading && items.length > 0 ? (
              <div className="seller-table-wrap">
                <table className="admin-table">
                  <thead>
                    <tr>
                      <th>Nickname</th>
                      <th>Contact</th>
                      <th>Address</th>
                      <th>City</th>
                      <th>PIN</th>
                    </tr>
                  </thead>
                  <tbody>
                    {items.map((row) => (
                      <tr key={row.id}>
                        <td>
                          <strong>{row.pickupLocation}</strong>
                        </td>
                        <td>
                          {row.name}
                          <br />
                          <span className="admin-muted">{row.phone}</span>
                        </td>
                        <td>
                          {row.address}
                          {row.address2 ? (
                            <>
                              <br />
                              <span className="admin-muted">{row.address2}</span>
                            </>
                          ) : null}
                        </td>
                        <td>
                          {row.city}, {row.state}
                        </td>
                        <td>{row.pinCode}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ) : null}

            {!loading && items.length === 0 ? (
              <p className="admin-muted">No pickup locations yet. Add your first address below.</p>
            ) : null}

            {atMax ? (
              <div className="seller-status seller-status--approved" role="status">
                <strong>Maximum reached</strong>
                <span>You already have {maxAllowed} pickup locations. Remove one in Shiprocket/support if you need to change.</span>
              </div>
            ) : (
              <form className="seller-form" onSubmit={onSubmit} noValidate>
                <h2 className="seller-lead" style={{ marginTop: 0 }}>
                  Add pickup location
                </h2>
                <div className="form-grid">
                  <div className="form-field">
                    <label htmlFor="pickup-nickname">Pickup nickname</label>
                    <input
                      id="pickup-nickname"
                      required
                      maxLength={36}
                      value={form.pickupLocation}
                      onChange={onChange('pickupLocation')}
                      placeholder="e.g. Home or Work"
                    />
                  </div>
                  <div className="form-field">
                    <label htmlFor="pickup-name">Contact / shipper name</label>
                    <input
                      id="pickup-name"
                      required
                      maxLength={100}
                      value={form.name}
                      onChange={onChange('name')}
                    />
                  </div>
                  <div className="form-field">
                    <label htmlFor="pickup-email">Email</label>
                    <input
                      id="pickup-email"
                      type="email"
                      required
                      maxLength={100}
                      value={form.email}
                      onChange={onChange('email')}
                    />
                  </div>
                  <div className="form-field">
                    <label htmlFor="pickup-phone">Phone (10-digit)</label>
                    <input
                      id="pickup-phone"
                      type="tel"
                      required
                      inputMode="numeric"
                      maxLength={10}
                      value={form.phone}
                      onChange={onChange('phone')}
                      placeholder="9876543210"
                    />
                  </div>
                  <div className="form-field full">
                    <label htmlFor="pickup-address">Address (max 80)</label>
                    <input
                      id="pickup-address"
                      required
                      maxLength={80}
                      value={form.address}
                      onChange={onChange('address')}
                    />
                  </div>
                  <div className="form-field full">
                    <label htmlFor="pickup-address2">Address line 2 (optional)</label>
                    <input
                      id="pickup-address2"
                      maxLength={80}
                      value={form.address2}
                      onChange={onChange('address2')}
                    />
                  </div>
                  <div className="form-field">
                    <label htmlFor="pickup-city">City</label>
                    <input
                      id="pickup-city"
                      required
                      maxLength={50}
                      value={form.city}
                      onChange={onChange('city')}
                    />
                  </div>
                  <div className="form-field">
                    <label htmlFor="pickup-state">State</label>
                    <input
                      id="pickup-state"
                      required
                      maxLength={50}
                      value={form.state}
                      onChange={onChange('state')}
                    />
                  </div>
                  <div className="form-field">
                    <label htmlFor="pickup-country">Country</label>
                    <input
                      id="pickup-country"
                      maxLength={50}
                      value={form.country}
                      onChange={onChange('country')}
                    />
                  </div>
                  <div className="form-field">
                    <label htmlFor="pickup-pin">Pin code</label>
                    <input
                      id="pickup-pin"
                      required
                      inputMode="numeric"
                      pattern="\d{6}"
                      maxLength={6}
                      value={form.pinCode}
                      onChange={onChange('pinCode')}
                    />
                  </div>
                  <div className="form-field">
                    <label htmlFor="pickup-lat">Latitude (optional)</label>
                    <input
                      id="pickup-lat"
                      maxLength={30}
                      value={form.lat}
                      onChange={onChange('lat')}
                      placeholder="Optional"
                    />
                  </div>
                  <div className="form-field">
                    <label htmlFor="pickup-long">Longitude (optional)</label>
                    <input
                      id="pickup-long"
                      maxLength={30}
                      value={form.long}
                      onChange={onChange('long')}
                      placeholder="Optional"
                    />
                  </div>
                  <div className="form-field">
                    <label htmlFor="pickup-gstin">GSTIN (optional)</label>
                    <input
                      id="pickup-gstin"
                      maxLength={15}
                      value={form.gstin}
                      onChange={onChange('gstin')}
                    />
                  </div>
                </div>
                <div className="seller-form-actions">
                  <button type="submit" className="btn btn-primary" disabled={saving}>
                    {saving ? 'Creating…' : 'Create pickup'}
                  </button>
                </div>
              </form>
            )}
          </>
        )}
      </div>
    </div>
  )
}
