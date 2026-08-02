import { useEffect, useState } from 'react'
import { api } from '../api/client'
import { buildShippingAddressPayload } from '../utils/payloads'
import { useCustomerAuth } from '../context/CustomerAuthContext'

const EMPTY_FORM = {
  label: '',
  firstName: '',
  lastName: '',
  email: '',
  phone: '',
  address: '',
  city: '',
  state: '',
  zip: '',
  country: 'India',
  isDefault: false,
}

export default function Addresses() {
  const { user } = useCustomerAuth()
  const [addresses, setAddresses] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const [showForm, setShowForm] = useState(false)
  const [editingId, setEditingId] = useState(null)
  const [form, setForm] = useState(EMPTY_FORM)
  const [saving, setSaving] = useState(false)
  const [formError, setFormError] = useState('')
  const [busyId, setBusyId] = useState(null)

  const load = () => {
    setLoading(true)
    setError('')
    return api
      .getShippingAddresses()
      .then((list) => setAddresses(list || []))
      .catch((err) => setError(err.message || 'Unable to load your addresses.'))
      .finally(() => setLoading(false))
  }

  useEffect(() => {
    load()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const openNewForm = () => {
    setEditingId(null)
    setForm({ ...EMPTY_FORM, email: user?.email || '' })
    setFormError('')
    setShowForm(true)
  }

  const openEditForm = (addr) => {
    setEditingId(addr.id)
    setForm({
      label: addr.label || '',
      firstName: addr.firstName || '',
      lastName: addr.lastName || '',
      email: addr.email || '',
      phone: addr.phone || '',
      address: addr.address || '',
      city: addr.city || '',
      state: addr.state || '',
      zip: addr.zip || '',
      country: addr.country || 'India',
      isDefault: Boolean(addr.isDefault),
    })
    setFormError('')
    setShowForm(true)
  }

  const closeForm = () => {
    setShowForm(false)
    setEditingId(null)
    setFormError('')
  }

  const onFieldChange = (e) => {
    const { name, value, type, checked } = e.target
    setForm((prev) => ({ ...prev, [name]: type === 'checkbox' ? checked : value }))
  }

  const onSubmit = async (e) => {
    e.preventDefault()
    setSaving(true)
    setFormError('')
    try {
      const payload = buildShippingAddressPayload(form)
      if (editingId) {
        await api.updateShippingAddress(editingId, payload)
      } else {
        await api.createShippingAddress(payload)
      }
      closeForm()
      await load()
    } catch (err) {
      setFormError(err.message || 'Unable to save this address.')
    } finally {
      setSaving(false)
    }
  }

  const onSetDefault = async (addr) => {
    setBusyId(addr.id)
    try {
      await api.setDefaultShippingAddress(addr.id)
      await load()
    } catch (err) {
      setError(err.message || 'Unable to set this address as default.')
    } finally {
      setBusyId(null)
    }
  }

  const onDelete = async (addr) => {
    if (!window.confirm('Remove this address?')) return
    setBusyId(addr.id)
    try {
      await api.deleteShippingAddress(addr.id)
      await load()
    } catch (err) {
      setError(err.message || 'Unable to delete this address.')
    } finally {
      setBusyId(null)
    }
  }

  return (
    <section className="section" style={{ paddingTop: 0 }}>
      <div className="container">
        <div className="page-hero addresses-hero">
          <div>
            <span className="eyebrow">Your account</span>
            <h1>Shipping addresses</h1>
          </div>
          {!showForm ? (
            <button type="button" className="btn btn-brass btn-sm" onClick={openNewForm}>
              Add address
            </button>
          ) : null}
        </div>

        {error ? <p className="admin-error">{error}</p> : null}

        {showForm ? (
          <form className="form-card address-form" onSubmit={onSubmit}>
            <h2>{editingId ? 'Edit address' : 'New address'}</h2>
            {formError ? <p className="admin-error">{formError}</p> : null}
            <div className="form-grid">
              <div className="form-field">
                <label htmlFor="addr-label">Label (optional)</label>
                <input
                  id="addr-label"
                  name="label"
                  placeholder="Home, Work…"
                  value={form.label}
                  onChange={onFieldChange}
                />
              </div>
              <div className="form-field">
                <label htmlFor="addr-phone">Phone (optional)</label>
                <input id="addr-phone" name="phone" value={form.phone} onChange={onFieldChange} />
              </div>
              <div className="form-field">
                <label htmlFor="addr-firstName">First name</label>
                <input
                  id="addr-firstName"
                  name="firstName"
                  required
                  value={form.firstName}
                  onChange={onFieldChange}
                />
              </div>
              <div className="form-field">
                <label htmlFor="addr-lastName">Last name</label>
                <input
                  id="addr-lastName"
                  name="lastName"
                  required
                  value={form.lastName}
                  onChange={onFieldChange}
                />
              </div>
              <div className="form-field full">
                <label htmlFor="addr-email">Email</label>
                <input
                  id="addr-email"
                  name="email"
                  type="email"
                  required
                  value={form.email}
                  onChange={onFieldChange}
                />
              </div>
              <div className="form-field full">
                <label htmlFor="addr-address">Address</label>
                <input
                  id="addr-address"
                  name="address"
                  required
                  value={form.address}
                  onChange={onFieldChange}
                />
              </div>
              <div className="form-field">
                <label htmlFor="addr-city">City</label>
                <input id="addr-city" name="city" required value={form.city} onChange={onFieldChange} />
              </div>
              <div className="form-field">
                <label htmlFor="addr-state">State</label>
                <input
                  id="addr-state"
                  name="state"
                  required
                  value={form.state}
                  onChange={onFieldChange}
                />
              </div>
              <div className="form-field">
                <label htmlFor="addr-zip">ZIP / PIN code</label>
                <input id="addr-zip" name="zip" required value={form.zip} onChange={onFieldChange} />
              </div>
              <div className="form-field">
                <label htmlFor="addr-country">Country</label>
                <input
                  id="addr-country"
                  name="country"
                  required
                  value={form.country}
                  onChange={onFieldChange}
                />
              </div>
            </div>

            <label className="save-address-check">
              <input
                type="checkbox"
                name="isDefault"
                checked={form.isDefault}
                onChange={onFieldChange}
              />
              Set as default address
            </label>

            <div className="profile-edit-actions">
              <button type="submit" className="btn btn-primary btn-sm" disabled={saving}>
                {saving ? 'Saving…' : 'Save address'}
              </button>
              <button type="button" className="btn-ghost" onClick={closeForm} disabled={saving}>
                Cancel
              </button>
            </div>
          </form>
        ) : null}

        {loading ? (
          <p className="admin-muted">Loading your addresses…</p>
        ) : addresses.length === 0 && !showForm ? (
          <div className="empty-state">
            <h2>No saved addresses</h2>
            <p>Add an address so checkout is faster next time.</p>
            <button type="button" className="btn btn-primary" onClick={openNewForm}>
              Add your first address
            </button>
          </div>
        ) : addresses.length > 0 ? (
          <div className="saved-addresses__list addresses-list">
            {addresses.map((addr) => (
              <div className="saved-address-card addresses-card" key={addr.id}>
                <span className="saved-address-card__top">
                  <strong>{addr.label || `${addr.firstName} ${addr.lastName}`}</strong>
                  {addr.isDefault ? (
                    <span className="saved-address-card__badge">Default</span>
                  ) : null}
                </span>
                <span className="saved-address-card__body">
                  {addr.firstName} {addr.lastName}
                  <br />
                  {addr.address}, {addr.city}, {addr.state} {addr.zip}, {addr.country}
                  {addr.phone ? (
                    <>
                      <br />
                      {addr.phone}
                    </>
                  ) : null}
                </span>
                <div className="addresses-card__actions">
                  {!addr.isDefault ? (
                    <button
                      type="button"
                      className="btn-ghost"
                      disabled={busyId === addr.id}
                      onClick={() => onSetDefault(addr)}
                    >
                      Set default
                    </button>
                  ) : null}
                  <button
                    type="button"
                    className="btn-ghost"
                    disabled={busyId === addr.id}
                    onClick={() => openEditForm(addr)}
                  >
                    Edit
                  </button>
                  <button
                    type="button"
                    className="remove-btn"
                    disabled={busyId === addr.id}
                    onClick={() => onDelete(addr)}
                  >
                    Delete
                  </button>
                </div>
              </div>
            ))}
          </div>
        ) : null}
      </div>
    </section>
  )
}
