import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import { useSellerAuth } from '../context/SellerAuthContext'

const emptyForm = {
  name: '',
  businessName: '',
  phone: '',
  addressLine1: '',
  addressLine2: '',
  city: '',
  state: '',
  pincode: '',
  gstin: '',
  description: '',
  upiId: '',
}

function statusHint(status) {
  switch ((status || '').toLowerCase()) {
    case 'approved':
      return 'Your account is approved. You can keep your details up to date anytime.'
    case 'rejected':
      return 'Your application was not approved. Update your details and submit again for review.'
    case 'suspended':
      return 'Your seller account is suspended. Contact Bagly support for help.'
    default:
      return 'Complete and submit your business details. An admin will review your account before you can list products.'
  }
}

function statusClass(status) {
  const s = (status || '').toLowerCase()
  if (s === 'approved') return 'seller-status seller-status--approved'
  if (s === 'rejected') return 'seller-status seller-status--rejected'
  if (s === 'suspended') return 'seller-status seller-status--suspended'
  return 'seller-status seller-status--pending'
}

export default function SellerDashboard() {
  const { user, logout, updateProfile } = useSellerAuth()
  const [form, setForm] = useState(emptyForm)
  const [status, setStatus] = useState(user?.status || 'Pending')
  const [rejectionReason, setRejectionReason] = useState('')
  const [profileComplete, setProfileComplete] = useState(false)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [success, setSuccess] = useState('')

  useEffect(() => {
    let cancelled = false

    async function load() {
      setLoading(true)
      setError('')
      try {
        const profile = await api.sellerGetProfile()
        if (cancelled) return
        setForm({
          name: profile.name || '',
          businessName: profile.businessName || '',
          phone: profile.phone || '',
          addressLine1: profile.addressLine1 || '',
          addressLine2: profile.addressLine2 || '',
          city: profile.city || '',
          state: profile.state || '',
          pincode: profile.pincode || '',
          gstin: profile.gstin || '',
          description: profile.description || '',
          upiId: profile.upiId || '',
        })
        setStatus(profile.status || 'Pending')
        setRejectionReason(profile.rejectionReason || '')
        setProfileComplete(Boolean(profile.profileComplete))
      } catch (err) {
        if (!cancelled) setError(err.message || 'Unable to load your seller profile.')
      } finally {
        if (!cancelled) setLoading(false)
      }
    }

    load()
    return () => {
      cancelled = true
    }
  }, [])

  const onChange = (field) => (e) => {
    setForm((prev) => ({ ...prev, [field]: e.target.value }))
  }

  const onSubmit = async (e) => {
    e.preventDefault()
    setSaving(true)
    setError('')
    setSuccess('')
    try {
      const profile = await updateProfile({
        name: form.name.trim(),
        businessName: form.businessName.trim(),
        phone: form.phone.trim(),
        addressLine1: form.addressLine1.trim(),
        addressLine2: form.addressLine2.trim() || null,
        city: form.city.trim(),
        state: form.state.trim(),
        pincode: form.pincode.trim(),
        gstin: form.gstin.trim() || null,
        description: form.description.trim() || null,
        upiId: form.upiId.trim() || null,
      })
      setStatus(profile.status || 'Pending')
      setRejectionReason(profile.rejectionReason || '')
      setProfileComplete(Boolean(profile.profileComplete))
      setSuccess(
        profile.status === 'Approved'
          ? 'Your business details were updated.'
          : 'Details submitted. Your account is pending admin review.',
      )
    } catch (err) {
      setError(err.message || 'Unable to save your details.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="seller-page">
      <div className="seller-shell">
        <header className="seller-head">
          <div>
            <p className="eyebrow">Seller hub</p>
            <h1>Business details</h1>
            <p className="seller-lead">
              Tell us about your business so Bagly can review and approve your seller account.
            </p>
          </div>
          <button type="button" className="btn btn-ghost" onClick={logout}>
            Sign out
          </button>
        </header>

        <div className={statusClass(status)} role="status">
          <strong>Status: {status || 'Pending'}</strong>
          <span>{statusHint(status)}</span>
          {status === 'Rejected' && rejectionReason ? (
            <span className="seller-reject-reason">Reason: {rejectionReason}</span>
          ) : null}
          {profileComplete && status === 'Pending' ? (
            <span>Your details are on file and waiting for approval.</span>
          ) : null}
        </div>

        {loading ? (
          <p className="admin-muted">Loading your details…</p>
        ) : (
          <form className="seller-form" onSubmit={onSubmit}>
            {error ? <p className="admin-error">{error}</p> : null}
            {success ? <p className="profile-success">{success}</p> : null}

            <div className="form-grid">
              <div className="form-field">
                <label htmlFor="seller-name">Contact name</label>
                <input id="seller-name" required value={form.name} onChange={onChange('name')} />
              </div>
              <div className="form-field">
                <label htmlFor="seller-business">Business display name</label>
                <input
                  id="seller-business"
                  required
                  value={form.businessName}
                  onChange={onChange('businessName')}
                />
              </div>
              <div className="form-field">
                <label htmlFor="seller-email">Email</label>
                <input id="seller-email" type="email" value={user?.email || ''} disabled readOnly />
              </div>
              <div className="form-field">
                <label htmlFor="seller-phone">Contact phone</label>
                <input
                  id="seller-phone"
                  type="tel"
                  required
                  value={form.phone}
                  onChange={onChange('phone')}
                  placeholder="10-digit mobile"
                  autoComplete="tel"
                />
              </div>
              <div className="form-field full">
                <label htmlFor="seller-address1">Business address</label>
                <input
                  id="seller-address1"
                  required
                  value={form.addressLine1}
                  onChange={onChange('addressLine1')}
                  placeholder="Address line 1"
                  autoComplete="address-line1"
                />
              </div>
              <div className="form-field full">
                <label htmlFor="seller-address2">Address line 2 (optional)</label>
                <input
                  id="seller-address2"
                  value={form.addressLine2}
                  onChange={onChange('addressLine2')}
                  autoComplete="address-line2"
                />
              </div>
              <div className="form-field">
                <label htmlFor="seller-city">City</label>
                <input
                  id="seller-city"
                  required
                  value={form.city}
                  onChange={onChange('city')}
                  autoComplete="address-level2"
                />
              </div>
              <div className="form-field">
                <label htmlFor="seller-state">State</label>
                <input
                  id="seller-state"
                  required
                  value={form.state}
                  onChange={onChange('state')}
                  autoComplete="address-level1"
                />
              </div>
              <div className="form-field">
                <label htmlFor="seller-pincode">Pincode</label>
                <input
                  id="seller-pincode"
                  required
                  value={form.pincode}
                  onChange={onChange('pincode')}
                  inputMode="numeric"
                  pattern="\d{6}"
                  maxLength={6}
                  autoComplete="postal-code"
                />
              </div>
              <div className="form-field">
                <label htmlFor="seller-gstin">GSTIN (optional, recommended)</label>
                <input
                  id="seller-gstin"
                  value={form.gstin}
                  onChange={onChange('gstin')}
                  placeholder="15-character GSTIN"
                  maxLength={15}
                />
              </div>
              <div className="form-field">
                <label htmlFor="seller-upi">UPI ID for payouts (optional)</label>
                <input
                  id="seller-upi"
                  value={form.upiId}
                  onChange={onChange('upiId')}
                  placeholder="name@upi"
                />
              </div>
              <div className="form-field full">
                <label htmlFor="seller-description">Short business description (optional)</label>
                <textarea
                  id="seller-description"
                  rows={3}
                  maxLength={500}
                  value={form.description}
                  onChange={onChange('description')}
                  placeholder="What you sell, materials, or your craft story…"
                />
              </div>
            </div>

            <div className="seller-form-actions">
              <button type="submit" className="btn btn-primary" disabled={saving}>
                {saving ? 'Saving…' : profileComplete ? 'Save & submit' : 'Submit for review'}
              </button>
              <Link to="/" className="btn btn-ghost">
                ← Back to store
              </Link>
            </div>
          </form>
        )}
      </div>
    </div>
  )
}
