import { useState } from 'react'
import { useCustomerAuth } from '../context/CustomerAuthContext'

export default function Profile() {
  const { user, updateProfile } = useCustomerAuth()
  const [editing, setEditing] = useState(false)
  const [name, setName] = useState(user?.name || '')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [success, setSuccess] = useState('')

  const startEdit = () => {
    setName(user?.name || '')
    setError('')
    setSuccess('')
    setEditing(true)
  }

  const cancelEdit = () => {
    setEditing(false)
    setError('')
  }

  const onSubmit = async (e) => {
    e.preventDefault()
    const trimmed = name.trim()
    if (!trimmed) {
      setError('Name is required.')
      return
    }
    setSaving(true)
    setError('')
    try {
      await updateProfile(trimmed)
      setSuccess('Your name has been updated.')
      setEditing(false)
    } catch (err) {
      setError(err.message || 'Unable to update your name.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <section className="section" style={{ paddingTop: 0 }}>
      <div className="container">
        <div className="page-hero">
          <span className="eyebrow">Your account</span>
          <h1>Profile</h1>
        </div>

        <div className="form-card profile-card">
          {success ? <p className="profile-success">{success}</p> : null}
          {error ? <p className="admin-error">{error}</p> : null}

          {editing ? (
            <form onSubmit={onSubmit} className="profile-edit-form">
              <div className="form-field">
                <label htmlFor="profile-name">Full name</label>
                <input
                  id="profile-name"
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  autoFocus
                  required
                />
              </div>
              <div className="profile-edit-actions">
                <button type="submit" className="btn btn-primary btn-sm" disabled={saving}>
                  {saving ? 'Saving…' : 'Save'}
                </button>
                <button
                  type="button"
                  className="btn-ghost"
                  onClick={cancelEdit}
                  disabled={saving}
                >
                  Cancel
                </button>
              </div>
            </form>
          ) : (
            <dl className="profile-details">
              <div className="profile-details__row">
                <dt>Name</dt>
                <dd>
                  {user?.name || '—'}
                  <button type="button" className="btn-ghost profile-edit-link" onClick={startEdit}>
                    Edit
                  </button>
                </dd>
              </div>
              <div className="profile-details__row">
                <dt>Email</dt>
                <dd>{user?.email || '—'}</dd>
              </div>
            </dl>
          )}
        </div>
      </div>
    </section>
  )
}
