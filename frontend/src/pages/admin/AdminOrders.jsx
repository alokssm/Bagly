import { Fragment, useCallback, useEffect, useState } from 'react'
import { api } from '../../api/client'
import { formatPrice } from '../../utils/format'

const PAGE_SIZE = 50
const emptyResult = { items: [], totalCount: 0, totalPages: 0, page: 1, pageSize: PAGE_SIZE, todayCount: 0 }

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
  if (s === 'confirmed') return 'admin-pill on'
  if (s === 'awaitingpayment' || s === 'pending') return 'admin-pill'
  return 'admin-pill off'
}

export default function AdminOrders() {
  const [filters, setFilters] = useState({ from: '', to: '', search: '' })
  const [applied, setApplied] = useState({ from: '', to: '', search: '' })
  const [page, setPage] = useState(1)
  const [result, setResult] = useState(emptyResult)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [expandedId, setExpandedId] = useState(null)
  const [details, setDetails] = useState({})
  const [detailLoading, setDetailLoading] = useState('')
  const [shiprocketProbe, setShiprocketProbe] = useState(null)
  const [shiprocketProbeLoading, setShiprocketProbeLoading] = useState(false)
  const [retryingId, setRetryingId] = useState('')

  const load = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      const data = await api.adminGetOrders({
        page,
        pageSize: PAGE_SIZE,
        from: applied.from || undefined,
        to: applied.to || undefined,
        search: applied.search || undefined,
      })
      setResult({
        items: data.items || [],
        totalCount: data.totalCount || 0,
        totalPages: data.totalPages || 0,
        page: data.page || page,
        pageSize: data.pageSize || PAGE_SIZE,
        todayCount: data.todayCount || 0,
      })
    } catch (err) {
      setError(err.message || 'Unable to load orders.')
      setResult(emptyResult)
    } finally {
      setLoading(false)
    }
  }, [page, applied])

  useEffect(() => {
    load()
  }, [load])

  const applyFilters = (e) => {
    e.preventDefault()
    setPage(1)
    setExpandedId(null)
    setApplied({ ...filters })
  }

  const clearFilters = () => {
    const empty = { from: '', to: '', search: '' }
    setFilters(empty)
    setApplied(empty)
    setPage(1)
    setExpandedId(null)
  }

  const toggleExpand = async (order) => {
    if (expandedId === order.id) {
      setExpandedId(null)
      return
    }
    setExpandedId(order.id)
    if (!details[order.id]) {
      setDetailLoading(order.id)
      try {
        const data = await api.adminGetOrder(order.id)
        setDetails((prev) => ({ ...prev, [order.id]: data }))
      } catch (err) {
        setDetails((prev) => ({ ...prev, [order.id]: { error: err.message || 'Unable to load order.' } }))
      } finally {
        setDetailLoading('')
      }
    }
  }

  const probeShiprocket = async () => {
    setShiprocketProbeLoading(true)
    try {
      const data = await api.adminShiprocketConnection()
      setShiprocketProbe(data)
    } catch (err) {
      setShiprocketProbe({ loginOk: false, loginError: err.message || 'Unable to probe Shiprocket.' })
    } finally {
      setShiprocketProbeLoading(false)
    }
  }

  const retryShiprocket = async (orderId) => {
    setRetryingId(orderId)
    try {
      const data = await api.adminRetryShiprocket(orderId)
      setDetails((prev) => ({ ...prev, [orderId]: data }))
      await load()
    } catch (err) {
      setDetails((prev) => ({
        ...prev,
        [orderId]: { ...(prev[orderId] || {}), error: err.message || 'Shiprocket retry failed.' },
      }))
    } finally {
      setRetryingId('')
    }
  }

  const { items: orders, totalCount, totalPages, todayCount } = result
  const from = totalCount === 0 ? 0 : (result.page - 1) * result.pageSize + 1
  const to = Math.min(result.page * result.pageSize, totalCount)

  return (
    <div className="admin-page">
      <div className="admin-page-head">
        <div>
          <p className="eyebrow">Sales</p>
          <h1>Orders</h1>
          <p className="admin-subtitle">
            50 orders per page. "Today" is measured in India Standard Time (Asia/Kolkata, UTC+5:30).
          </p>
        </div>
        <button
          type="button"
          className="btn btn-secondary"
          onClick={probeShiprocket}
          disabled={shiprocketProbeLoading}
        >
          {shiprocketProbeLoading ? 'Checking Shiprocket…' : 'Test Shiprocket'}
        </button>
      </div>

      {error ? <p className="admin-error">{error}</p> : null}

      {shiprocketProbe ? (
        <div className={`admin-banner ${shiprocketProbe.loginOk && shiprocketProbe.configuredPickupMatched ? 'ok' : 'warn'}`}>
          <p>
            <strong>Shiprocket login:</strong>{' '}
            {shiprocketProbe.loginOk ? 'OK' : `failed — ${shiprocketProbe.loginError || 'unknown error'}`}
          </p>
          <p>
            <strong>Configured pickup:</strong> {shiprocketProbe.configuredPickup || '(not set)'}{' '}
            {shiprocketProbe.loginOk
              ? shiprocketProbe.configuredPickupMatched
                ? '· matches'
                : '· does not match (case-sensitive)'
              : null}
          </p>
          {shiprocketProbe.pickupNicknames?.length ? (
            <p>
              <strong>Shiprocket nicknames:</strong> {shiprocketProbe.pickupNicknames.join(', ')}
            </p>
          ) : null}
          {shiprocketProbe.hint ? <p className="admin-muted">{shiprocketProbe.hint}</p> : null}
          {shiprocketProbe.pickupListError ? (
            <p className="admin-error">Pickup list: {shiprocketProbe.pickupListError}</p>
          ) : null}
        </div>
      ) : null}

      <div className="admin-stats admin-stats-2">
        <div className="admin-stat">
          <span>Orders today (IST)</span>
          <strong>{todayCount}</strong>
        </div>
        <div className="admin-stat">
          <span>Orders matching filters</span>
          <strong>{totalCount}</strong>
        </div>
      </div>

      <form className="admin-filters" onSubmit={applyFilters}>
        <label>
          From
          <input
            type="date"
            value={filters.from}
            onChange={(e) => setFilters((f) => ({ ...f, from: e.target.value }))}
          />
        </label>
        <label>
          To
          <input
            type="date"
            value={filters.to}
            onChange={(e) => setFilters((f) => ({ ...f, to: e.target.value }))}
          />
        </label>
        <label>
          Search
          <input
            type="search"
            value={filters.search}
            onChange={(e) => setFilters((f) => ({ ...f, search: e.target.value }))}
            placeholder="Order #, name, or email…"
          />
        </label>
        <div className="admin-filters-actions">
          <button type="submit" className="btn btn-primary">
            Apply
          </button>
          <button type="button" className="btn btn-secondary" onClick={clearFilters}>
            Clear
          </button>
        </div>
      </form>

      <div className="admin-table-wrap">
        {loading ? (
          <p className="admin-muted">Loading orders…</p>
        ) : orders.length === 0 ? (
          <p className="admin-muted">No orders found for the current filters.</p>
        ) : (
          <table className="admin-table">
            <thead>
              <tr>
                <th>Order #</th>
                <th>Customer</th>
                <th>Email</th>
                <th>Items</th>
                <th>Total</th>
                <th>Status</th>
                <th>Payment</th>
                <th>Shiprocket</th>
                <th>Created</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {orders.map((order) => (
                <Fragment key={order.id}>
                  <tr>
                    <td>{order.orderNumber}</td>
                    <td>{order.customerName || '—'}</td>
                    <td>{order.email}</td>
                    <td>{order.itemCount}</td>
                    <td>{formatPrice(order.total)}</td>
                    <td>
                      <span className={statusClass(order.status)}>{order.status}</span>
                    </td>
                    <td>
                      {order.paymentStatus}
                      {order.paymentProvider ? <small> / {order.paymentProvider}</small> : null}
                    </td>
                    <td className="nowrap">
                      {order.shiprocketShipmentCount > 0 ? (
                        <span
                          className={`admin-pill ${
                            order.shiprocketShipmentSuccessCount >= order.shiprocketShipmentCount ? 'on' : 'off'
                          }`}
                          title={order.shiprocketLastError || order.shiprocketStatus || ''}
                        >
                          {order.shiprocketShipmentSuccessCount}/{order.shiprocketShipmentCount} pickups
                        </span>
                      ) : order.shiprocketOrderId ? (
                        <span className="admin-pill on" title={order.shiprocketStatus || ''}>
                          {order.shiprocketOrderId}
                        </span>
                      ) : order.shiprocketLastError ? (
                        <span
                          className="admin-pill off"
                          title={order.shiprocketLastError}
                        >
                          {order.shiprocketStatus || 'Error'}
                        </span>
                      ) : (
                        <span className="admin-muted">—</span>
                      )}
                    </td>
                    <td className="nowrap">{formatDateTime(order.createdAt)}</td>
                    <td>
                      <button type="button" className="btn btn-secondary btn-sm" onClick={() => toggleExpand(order)}>
                        {expandedId === order.id ? 'Hide' : 'Items'}
                      </button>
                    </td>
                  </tr>
                  {expandedId === order.id ? (
                    <tr className="log-detail-row">
                      <td colSpan={10}>
                        <div className="log-detail">
                          {detailLoading === order.id ? (
                            <p className="admin-muted">Loading order details…</p>
                          ) : details[order.id]?.error ? (
                            <p className="admin-error">{details[order.id].error}</p>
                          ) : details[order.id] ? (
                            <>
                              <p>
                                <strong>Ship to:</strong> {details[order.id].firstName} {details[order.id].lastName}
                                {details[order.id].phone ? (
                                  <>
                                    {' '}
                                    · <strong>Phone:</strong> {details[order.id].phone}
                                  </>
                                ) : (
                                  <>
                                    {' '}
                                    · <span className="admin-muted">Phone missing (Shiprocket skipped)</span>
                                  </>
                                )}
                              </p>
                              <p>
                                <strong>Shiprocket (primary):</strong>{' '}
                                {details[order.id].shiprocketOrderId
                                  ? `#${details[order.id].shiprocketOrderId}`
                                  : 'not created'}
                                {details[order.id].shiprocketShipmentId
                                  ? ` · shipment ${details[order.id].shiprocketShipmentId}`
                                  : null}
                                {details[order.id].shiprocketStatus
                                  ? ` · ${details[order.id].shiprocketStatus}`
                                  : null}
                              </p>
                              {details[order.id].shiprocketShipments?.length ? (
                                <div style={{ marginBottom: '0.75rem' }}>
                                  <strong>Pickups / shipments:</strong>
                                  <ul style={{ margin: '0.35rem 0 0', paddingLeft: '1.2rem' }}>
                                    {details[order.id].shiprocketShipments.map((s) => (
                                      <li key={s.id}>
                                        <code>{s.pickupLocation}</code>
                                        {s.shiprocketOrderId
                                          ? ` · SR #${s.shiprocketOrderId}`
                                          : ' · not created'}
                                        {s.shiprocketShipmentId ? ` · shipment ${s.shiprocketShipmentId}` : null}
                                        {s.status ? ` · ${s.status}` : null}
                                        {s.lastError ? (
                                          <span className="admin-error"> — {s.lastError}</span>
                                        ) : null}
                                      </li>
                                    ))}
                                  </ul>
                                </div>
                              ) : null}
                              {details[order.id].shiprocketLastError ? (
                                <p className="admin-error">
                                  <strong>Shiprocket error:</strong> {details[order.id].shiprocketLastError}
                                </p>
                              ) : null}
                              {(() => {
                                const shipments = details[order.id].shiprocketShipments || []
                                const allOk =
                                  shipments.length > 0 &&
                                  shipments.every((s) => s.shiprocketOrderId) &&
                                  !details[order.id].shiprocketLastError
                                const legacyOk =
                                  !shipments.length && Boolean(details[order.id].shiprocketOrderId)
                                if (allOk || legacyOk) return null
                                return (
                                  <p>
                                    <button
                                      type="button"
                                      className="btn btn-secondary btn-sm"
                                      disabled={retryingId === order.id}
                                      onClick={() => retryShiprocket(order.id)}
                                    >
                                      {retryingId === order.id
                                        ? 'Retrying Shiprocket…'
                                        : 'Retry failed Shiprocket pickups'}
                                    </button>
                                  </p>
                                )
                              })()}
                              <table className="admin-table">
                                <thead>
                                  <tr>
                                    <th>Product</th>
                                    <th>Color</th>
                                    <th>Qty</th>
                                    <th>Unit price</th>
                                    <th>Line total</th>
                                  </tr>
                                </thead>
                                <tbody>
                                  {details[order.id].items.map((item, idx) => (
                                    <tr key={`${item.productId}-${idx}`}>
                                      <td>{item.productName}</td>
                                      <td>{item.color}</td>
                                      <td>{item.quantity}</td>
                                      <td>{formatPrice(item.unitPrice)}</td>
                                      <td>{formatPrice(item.unitPrice * item.quantity)}</td>
                                    </tr>
                                  ))}
                                </tbody>
                              </table>
                            </>
                          ) : (
                            <p className="admin-muted">No details.</p>
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

      <div className="admin-pagination">
        <p className="admin-muted">
          {totalCount === 0 ? '0 orders' : `Showing ${from}–${to} of ${totalCount}`}
        </p>
        <div className="admin-pagination-controls">
          <button
            type="button"
            className="btn btn-secondary"
            disabled={page <= 1 || loading}
            onClick={() => {
              setExpandedId(null)
              setPage((p) => Math.max(1, p - 1))
            }}
          >
            Previous
          </button>
          <span>
            Page {totalPages === 0 ? 0 : page} of {totalPages}
          </span>
          <button
            type="button"
            className="btn btn-secondary"
            disabled={page >= totalPages || loading || totalPages === 0}
            onClick={() => {
              setExpandedId(null)
              setPage((p) => p + 1)
            }}
          >
            Next
          </button>
        </div>
      </div>
    </div>
  )
}
