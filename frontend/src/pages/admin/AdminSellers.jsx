import { Fragment, useCallback, useEffect, useState } from 'react'
import { api } from '../../api/client'

function formatDateTime(value) {
  if (!value) return '—'
  try {
    return new Date(value).toLocaleString()
  } catch {
    return String(value)
  }
}

function statusClass(status) {
  const s = (status || '').toLowerCase()
  if (s === 'approved') return 'admin-pill on'
  if (s === 'rejected' || s === 'suspended') return 'admin-pill off'
  return 'admin-pill'
}

export default function AdminSellers() {
  const [statusFilter, setStatusFilter] = useState('Pending')
  const [sellers, setSellers] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [busyId, setBusyId] = useState('')
  const [expandedId, setExpandedId] = useState(null)
  const [details, setDetails] = useState({})
  const [rejectDrafts, setRejectDrafts] = useState({})
  const [message, setMessage] = useState('')

  const load = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      const data = await api.adminGetSellers({
        status: statusFilter || undefined,
      })
      setSellers(Array.isArray(data) ? data : [])
    } catch (err) {
      setError(err.message || 'Unable to load sellers.')
      setSellers([])
    } finally {
      setLoading(false)
    }
  }, [statusFilter])

  useEffect(() => {
    load()
  }, [load])

  const toggleExpand = async (seller) => {
    if (expandedId === seller.id) {
      setExpandedId(null)
      return
    }
    setExpandedId(seller.id)
    if (!details[seller.id]) {
      try {
        const data = await api.adminGetSeller(seller.id)
        setDetails((prev) => ({ ...prev, [seller.id]: data }))
      } catch (err) {
        setDetails((prev) => ({
          ...prev,
          [seller.id]: { error: err.message || 'Unable to load seller.' },
        }))
      }
    }
  }

  const approve = async (id) => {
    setBusyId(id)
    setMessage('')
    setError('')
    try {
      const updated = await api.adminApproveSeller(id)
      setMessage(`Approved ${updated.businessName || updated.email}.`)
      setDetails((prev) => ({ ...prev, [id]: updated }))
      await load()
    } catch (err) {
      setError(err.message || 'Unable to approve seller.')
    } finally {
      setBusyId('')
    }
  }

  const reject = async (id) => {
    setBusyId(id)
    setMessage('')
    setError('')
    try {
      const reason = (rejectDrafts[id] || '').trim()
      const updated = await api.adminRejectSeller(id, reason || null)
      setMessage(`Rejected ${updated.businessName || updated.email}.`)
      setDetails((prev) => ({ ...prev, [id]: updated }))
      setRejectDrafts((prev) => ({ ...prev, [id]: '' }))
      await load()
    } catch (err) {
      setError(err.message || 'Unable to reject seller.')
    } finally {
      setBusyId('')
    }
  }

  const pendingCount = sellers.filter((s) => s.status === 'Pending').length

  return (
    <div className="admin-page">
      <div className="admin-page-head">
        <div>
          <p className="eyebrow">Marketplace</p>
          <h1>Sellers</h1>
          <p className="admin-subtitle">
            Review seller applications. Approving notifies the seller by email so they can use the
            seller dashboard.
          </p>
        </div>
      </div>

      {error ? <p className="admin-error">{error}</p> : null}
      {message ? <p className="profile-success">{message}</p> : null}

      <div className="admin-stats admin-stats-2">
        <div className="admin-stat">
          <span>In this list</span>
          <strong>{sellers.length}</strong>
        </div>
        <div className="admin-stat">
          <span>Pending (visible when filter is Pending)</span>
          <strong>{statusFilter === 'Pending' ? pendingCount : '—'}</strong>
        </div>
      </div>

      <form
        className="admin-filters"
        onSubmit={(e) => {
          e.preventDefault()
        }}
      >
        <label>
          Status
          <select value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)}>
            <option value="Pending">Pending</option>
            <option value="Approved">Approved</option>
            <option value="Rejected">Rejected</option>
            <option value="Suspended">Suspended</option>
            <option value="">All</option>
          </select>
        </label>
        <div className="admin-filters-actions">
          <button type="button" className="btn btn-secondary" onClick={load}>
            Refresh
          </button>
        </div>
      </form>

      <div className="admin-table-wrap">
        {loading ? (
          <p className="admin-muted">Loading sellers…</p>
        ) : sellers.length === 0 ? (
          <p className="admin-muted">No sellers found for this filter.</p>
        ) : (
          <table className="admin-table">
            <thead>
              <tr>
                <th>Business</th>
                <th>Contact</th>
                <th>Email</th>
                <th>City</th>
                <th>Profile</th>
                <th>Status</th>
                <th>Submitted</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {sellers.map((seller) => (
                <Fragment key={seller.id}>
                  <tr>
                    <td>{seller.businessName}</td>
                    <td>{seller.name}</td>
                    <td>{seller.email}</td>
                    <td>
                      {[seller.city, seller.state].filter(Boolean).join(', ') || '—'}
                    </td>
                    <td>{seller.profileComplete ? 'Complete' : 'Incomplete'}</td>
                    <td>
                      <span className={statusClass(seller.status)}>{seller.status}</span>
                    </td>
                    <td className="nowrap">
                      {formatDateTime(seller.profileSubmittedAt || seller.createdAt)}
                    </td>
                    <td>
                      <div className="admin-row-actions">
                        <button
                          type="button"
                          className="btn btn-secondary btn-sm"
                          onClick={() => toggleExpand(seller)}
                        >
                          {expandedId === seller.id ? 'Hide' : 'Details'}
                        </button>
                        {seller.status !== 'Approved' ? (
                          <button
                            type="button"
                            className="btn btn-primary btn-sm"
                            disabled={busyId === seller.id}
                            onClick={() => approve(seller.id)}
                          >
                            {busyId === seller.id ? '…' : 'Approve'}
                          </button>
                        ) : null}
                      </div>
                    </td>
                  </tr>
                  {expandedId === seller.id ? (
                    <tr className="log-detail-row">
                      <td colSpan={8}>
                        <div className="log-detail">
                          {details[seller.id]?.error ? (
                            <p className="admin-error">{details[seller.id].error}</p>
                          ) : details[seller.id] ? (
                            <>
                              <p>
                                <strong>Phone:</strong> {details[seller.id].phone || '—'}
                              </p>
                              <p>
                                <strong>Address:</strong>{' '}
                                {[
                                  details[seller.id].addressLine1,
                                  details[seller.id].addressLine2,
                                  details[seller.id].city,
                                  details[seller.id].state,
                                  details[seller.id].pincode,
                                ]
                                  .filter(Boolean)
                                  .join(', ') || '—'}
                              </p>
                              <p>
                                <strong>GSTIN:</strong> {details[seller.id].gstin || '—'}
                              </p>
                              <p>
                                <strong>UPI:</strong> {details[seller.id].upiId || '—'}
                              </p>
                              <p>
                                <strong>Description:</strong>{' '}
                                {details[seller.id].description || '—'}
                              </p>
                              {details[seller.id].rejectionReason ? (
                                <p>
                                  <strong>Rejection reason:</strong>{' '}
                                  {details[seller.id].rejectionReason}
                                </p>
                              ) : null}
                              {seller.status !== 'Rejected' ? (
                                <div className="admin-seller-reject">
                                  <label htmlFor={`reject-${seller.id}`}>
                                    Reject with reason (optional)
                                  </label>
                                  <textarea
                                    id={`reject-${seller.id}`}
                                    rows={2}
                                    value={rejectDrafts[seller.id] || ''}
                                    onChange={(e) =>
                                      setRejectDrafts((prev) => ({
                                        ...prev,
                                        [seller.id]: e.target.value,
                                      }))
                                    }
                                    placeholder="Missing GST / unclear address / etc."
                                  />
                                  <button
                                    type="button"
                                    className="btn btn-secondary btn-sm"
                                    disabled={busyId === seller.id}
                                    onClick={() => reject(seller.id)}
                                  >
                                    Reject
                                  </button>
                                </div>
                              ) : null}
                            </>
                          ) : (
                            <p className="admin-muted">Loading seller details…</p>
                          )}
                        </div>
                      </td>
                    </tr>
                  ) : null}
                </Fragment>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  )
}
