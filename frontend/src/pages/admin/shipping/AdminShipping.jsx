import { useCallback, useEffect, useState } from 'react'
import { api } from '../../../api/client'
import { formatPrice, formatShippingPrice } from '../../../utils/format'

function formatDateTime(value) {
  if (!value) return '—'
  try {
    return new Date(value).toLocaleString()
  } catch {
    return String(value)
  }
}

function shippingPill(status, awb) {
  if (awb) return 'admin-pill on'
  if ((status || '').toLowerCase() === 'readytoship') return 'admin-pill'
  return 'admin-pill off'
}

function shipmentMatchesTab(shipment, tab) {
  if (tab === 'awb') return !!shipment.awbCode
  if (tab === 'ready') return !!shipment.readyToShipAt && !shipment.awbCode
  return !!shipment.shiprocketShipmentId && !shipment.readyToShipAt && !shipment.awbCode
}

function truncate(text, max = 120) {
  if (!text) return '—'
  return text.length <= max ? text : `${text.slice(0, max)}…`
}

function courierRateBreakdown(courier) {
  const freight = Number(courier.freightCharge ?? 0)
  const coverage = Number(courier.coverageCharge ?? 0)
  const whatsapp = Number(courier.whatsAppCharge ?? courier.whatsappCharge ?? 0)
  const cod = Number(courier.codCharge ?? 0)
  const parts = [
    freight ? `Freight ${formatShippingPrice(freight)}` : null,
    coverage ? `Coverage ${formatShippingPrice(coverage)}` : null,
    whatsapp ? `WhatsApp ${formatShippingPrice(whatsapp)}` : null,
    cod ? `COD ${formatShippingPrice(cod)}` : null,
  ].filter(Boolean)
  return parts.length ? parts.join(' · ') : null
}

export default function AdminShipping() {
  const [tab, setTab] = useState('new')
  const [result, setResult] = useState({
    items: [],
    totalCount: 0,
    tab: 'new',
    newCount: 0,
    readyCount: 0,
    awbCount: 0,
  })
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [actionError, setActionError] = useState('')
  const [busyShipmentId, setBusyShipmentId] = useState('')
  const [couriersByShipment, setCouriersByShipment] = useState({})
  const [courierMetaByShipment, setCourierMetaByShipment] = useState({})
  const [apiLogs, setApiLogs] = useState([])
  const [logsLoading, setLogsLoading] = useState(false)
  const [logsError, setLogsError] = useState('')
  const [expandedLogId, setExpandedLogId] = useState(null)
  const [logsFilterShipmentId, setLogsFilterShipmentId] = useState('')

  const load = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      const data = await api.adminGetShippingOrders({ tab })
      setResult({
        items: data.items || [],
        totalCount: data.totalCount || 0,
        tab: data.tab || tab,
        newCount: data.newCount || 0,
        readyCount: data.readyCount || 0,
        awbCount: data.awbCount || 0,
      })
    } catch (err) {
      setError(err.message || 'Unable to load shipping orders.')
      setResult((prev) => ({ ...prev, items: [], totalCount: 0 }))
    } finally {
      setLoading(false)
    }
  }, [tab])

  const loadLogs = useCallback(async (shipmentId) => {
    setLogsLoading(true)
    setLogsError('')
    try {
      const params = { take: 40 }
      if (shipmentId) params.shipmentId = shipmentId
      const data = await api.adminGetShippingApiLogs(params)
      setApiLogs(Array.isArray(data) ? data : [])
    } catch (err) {
      setLogsError(err.message || 'Unable to load API logs.')
      setApiLogs([])
    } finally {
      setLogsLoading(false)
    }
  }, [])

  useEffect(() => {
    load()
  }, [load])

  useEffect(() => {
    loadLogs(logsFilterShipmentId)
  }, [loadLogs, logsFilterShipmentId])

  const readyToShip = async (shipment) => {
    setBusyShipmentId(shipment.id)
    setActionError('')
    try {
      const data = await api.adminShippingReadyToShip(shipment.id)
      setCouriersByShipment((prev) => ({
        ...prev,
        [shipment.id]: data.couriers || [],
      }))
      setCourierMetaByShipment((prev) => ({
        ...prev,
        [shipment.id]: {
          pickupPostcode: data.pickupPostcode,
          deliveryPostcode: data.deliveryPostcode,
          weightKg: data.weightKg,
          length: data.length,
          breadth: data.breadth,
          height: data.height,
          declaredValue: data.declaredValue,
          cod: data.cod,
        },
      }))
      await load()
      await loadLogs(logsFilterShipmentId)
    } catch (err) {
      setActionError(err.message || 'Ready to Ship failed.')
    } finally {
      setBusyShipmentId('')
    }
  }

  const assignAwb = async (shipment, courier) => {
    setBusyShipmentId(shipment.id)
    setActionError('')
    try {
      await api.adminShippingAssignAwb(shipment.id, {
        courierId: courier.courierId,
        rate: courier.rate,
      })
      setCouriersByShipment((prev) => {
        const next = { ...prev }
        delete next[shipment.id]
        return next
      })
      await load()
      await loadLogs(logsFilterShipmentId)
    } catch (err) {
      setActionError(err.message || 'Assign AWB failed.')
    } finally {
      setBusyShipmentId('')
    }
  }

  const tabs = [
    { id: 'new', label: 'New', count: result.newCount },
    { id: 'ready', label: 'Ready to Ship', count: result.readyCount },
    { id: 'awb', label: 'AWB Assigned', count: result.awbCount },
  ]

  const rows = []
  for (const order of result.items) {
    for (const shipment of order.shipments || []) {
      if (!shipmentMatchesTab(shipment, tab)) continue
      rows.push({ order, shipment, highlight: true })
    }
  }

  return (
    <div className="admin-page">
      <div className="admin-page-head">
        <div>
          <p className="eyebrow">Fulfillment</p>
          <h1>Shipping</h1>
          <p className="admin-subtitle">
            Per-pickup Shiprocket shipments (home/work). Ready to Ship loads couriers; Assign AWB stores AWB + charge.
          </p>
        </div>
        <button type="button" className="btn btn-secondary" onClick={load} disabled={loading}>
          {loading ? 'Refreshing…' : 'Refresh'}
        </button>
      </div>

      {error ? <p className="admin-error">{error}</p> : null}
      {actionError ? <p className="admin-error">{actionError}</p> : null}

      <div className="admin-filters" style={{ gap: '0.5rem', flexWrap: 'wrap' }}>
        {tabs.map((t) => (
          <button
            key={t.id}
            type="button"
            className={`btn ${tab === t.id ? 'btn-primary' : 'btn-secondary'}`}
            onClick={() => {
              setTab(t.id)
              setActionError('')
            }}
          >
            {t.label} ({t.count})
          </button>
        ))}
      </div>

      {loading && !rows.length ? (
        <p className="admin-muted">Loading shipping orders…</p>
      ) : !rows.length ? (
        <p className="admin-muted">No orders in this tab. Confirmed orders need a successful Shiprocket create first.</p>
      ) : (
        <div className="admin-table-wrap">
          <table className="admin-table">
            <thead>
              <tr>
                <th>Order</th>
                <th>Customer</th>
                <th>Payment</th>
                <th>Pickup / Shipment</th>
                <th>Status</th>
                <th>AWB / Charge</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {rows.map(({ order, shipment, highlight }) => {
                const couriers = couriersByShipment[shipment.id] || []
                const meta = courierMetaByShipment[shipment.id]
                const busy = busyShipmentId === shipment.id
                const canReady = !!shipment.shiprocketShipmentId && !shipment.awbCode

                return (
                  <tr key={shipment.id} style={highlight ? undefined : { opacity: 0.55 }}>
                    <td>
                      <strong>{order.orderNumber}</strong>
                      <div className="admin-muted">{formatDateTime(order.createdAt)}</div>
                    </td>
                    <td>
                      {order.customerName}
                      <div className="admin-muted">{order.email}</div>
                      <div className="admin-muted">PIN {order.zip || '—'}</div>
                    </td>
                    <td>
                      {order.paymentProvider || '—'}
                      <div className="admin-muted">{formatPrice(order.total, order.currency)}</div>
                    </td>
                    <td>
                      <strong>{shipment.pickupLocation}</strong>
                      <div className="admin-muted">SR ship #{shipment.shiprocketShipmentId || '—'}</div>
                      <div className="admin-muted">SR order {shipment.shiprocketOrderId || '—'}</div>
                    </td>
                    <td>
                      <span className={shippingPill(shipment.shippingStatus, shipment.awbCode)}>
                        {shipment.awbCode
                          ? 'AWB Assigned'
                          : shipment.shippingStatus || shipment.status || 'Created'}
                      </span>
                      {shipment.lastError ? (
                        <div className="admin-error" style={{ marginTop: 4 }}>
                          {shipment.lastError}
                        </div>
                      ) : null}
                    </td>
                    <td>
                      {shipment.awbCode ? (
                        <>
                          <strong>{shipment.awbCode}</strong>
                          <div className="admin-muted">
                            {shipment.courierName || 'Courier'}
                            {shipment.courierId ? ` (#${shipment.courierId})` : ''}
                          </div>
                          <div>
                            {shipment.actualShippingCharge != null
                              ? formatShippingPrice(shipment.actualShippingCharge)
                              : '—'}
                          </div>
                        </>
                      ) : (
                        <span className="admin-muted">—</span>
                      )}
                    </td>
                    <td>
                      {canReady ? (
                        <button
                          type="button"
                          className="btn btn-primary"
                          disabled={busy}
                          onClick={() => readyToShip(shipment)}
                        >
                          {busy ? 'Loading…' : shipment.readyToShipAt ? 'Refresh Couriers' : 'Ready to Ship'}
                        </button>
                      ) : null}
                      <div style={{ marginTop: 8 }}>
                        <button
                          type="button"
                          className="btn btn-secondary"
                          onClick={() => {
                            setLogsFilterShipmentId(shipment.id)
                            setExpandedLogId(null)
                          }}
                        >
                          API logs
                        </button>
                      </div>
                      {couriers.length > 0 ? (
                        <div style={{ marginTop: 10, minWidth: 280 }}>
                          {meta ? (
                            <p className="admin-muted" style={{ marginBottom: 6 }}>
                              {meta.pickupPostcode} → {meta.deliveryPostcode} · {meta.weightKg} kg ·{' '}
                              {meta.length}×{meta.breadth}×{meta.height} cm · decl{' '}
                              {formatPrice(meta.declaredValue, order.currency)} ·{' '}
                              {meta.cod ? 'COD' : 'Prepaid'}
                            </p>
                          ) : null}
                          <table className="admin-table" style={{ fontSize: '0.9rem' }}>
                            <thead>
                              <tr>
                                <th>Name</th>
                                <th>Rate</th>
                                <th>Delivery ETA</th>
                                <th />
                              </tr>
                            </thead>
                            <tbody>
                              {couriers.map((c) => {
                                const breakdown = courierRateBreakdown(c)
                                return (
                                  <tr key={c.courierId}>
                                    <td>{c.courierName}</td>
                                    <td>
                                      <strong>{formatShippingPrice(c.rate)}</strong>
                                      {breakdown ? (
                                        <div
                                          className="admin-muted"
                                          style={{ fontSize: '0.75rem', marginTop: 2 }}
                                          title={breakdown}
                                        >
                                          {breakdown}
                                        </div>
                                      ) : null}
                                    </td>
                                    <td>
                                      {c.estimatedDelivery ||
                                        (c.estimatedDeliveryDays != null
                                          ? `${c.estimatedDeliveryDays} days`
                                          : '—')}
                                    </td>
                                    <td>
                                      <button
                                        type="button"
                                        className="btn btn-secondary"
                                        disabled={busy}
                                        onClick={() => assignAwb(shipment, c)}
                                      >
                                        Assign AWB
                                      </button>
                                    </td>
                                  </tr>
                                )
                              })}
                            </tbody>
                          </table>
                        </div>
                      ) : null}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}

      <section style={{ marginTop: '2rem' }}>
        <div className="admin-page-head" style={{ marginBottom: '0.75rem' }}>
          <div>
            <h2 style={{ margin: 0, fontSize: '1.15rem' }}>Shiprocket API logs</h2>
            <p className="admin-muted" style={{ margin: '0.25rem 0 0' }}>
              Exact request bodies / query strings sent to Shiprocket (tokens and passwords redacted).
              {logsFilterShipmentId ? ' Filtered to one shipment.' : ' Showing recent across all shipments.'}
            </p>
          </div>
          <div style={{ display: 'flex', gap: '0.5rem', flexWrap: 'wrap' }}>
            {logsFilterShipmentId ? (
              <button
                type="button"
                className="btn btn-secondary"
                onClick={() => {
                  setLogsFilterShipmentId('')
                  setExpandedLogId(null)
                }}
              >
                Clear filter
              </button>
            ) : null}
            <button
              type="button"
              className="btn btn-secondary"
              disabled={logsLoading}
              onClick={() => loadLogs(logsFilterShipmentId)}
            >
              {logsLoading ? 'Loading…' : 'Refresh logs'}
            </button>
          </div>
        </div>

        {logsError ? <p className="admin-error">{logsError}</p> : null}
        {logsLoading && !apiLogs.length ? (
          <p className="admin-muted">Loading API logs…</p>
        ) : !apiLogs.length ? (
          <p className="admin-muted">No Shiprocket API logs yet. Run Ready to Ship or Assign AWB to create one.</p>
        ) : (
          <div className="admin-table-wrap">
            <table className="admin-table">
              <thead>
                <tr>
                  <th>When</th>
                  <th>Action</th>
                  <th>HTTP</th>
                  <th>Status</th>
                  <th>Request</th>
                  <th>Admin</th>
                </tr>
              </thead>
              <tbody>
                {apiLogs.map((log) => {
                  const open = expandedLogId === log.id
                  return (
                    <tr key={log.id}>
                      <td>
                        <div>{formatDateTime(log.createdAtUtc)}</div>
                        <div className="admin-muted">#{log.id}</div>
                      </td>
                      <td>
                        <strong>{log.action}</strong>
                        <div className="admin-muted">{truncate(log.url, 48)}</div>
                      </td>
                      <td>{log.httpMethod}</td>
                      <td>{log.responseStatus ?? '—'}</td>
                      <td style={{ maxWidth: 420 }}>
                        <button
                          type="button"
                          className="btn btn-secondary"
                          style={{ marginBottom: 6 }}
                          onClick={() => setExpandedLogId(open ? null : log.id)}
                        >
                          {open ? 'Hide request' : 'View request'}
                        </button>
                        {open ? (
                          <pre
                            style={{
                              margin: 0,
                              whiteSpace: 'pre-wrap',
                              wordBreak: 'break-word',
                              fontSize: '0.8rem',
                              maxHeight: 280,
                              overflow: 'auto',
                            }}
                          >
                            {log.requestJson || '(empty)'}
                            {log.responseJson ? `\n\n--- response ---\n${log.responseJson}` : ''}
                          </pre>
                        ) : (
                          <div className="admin-muted">{truncate(log.requestJson, 100)}</div>
                        )}
                      </td>
                      <td className="admin-muted">{log.adminEmail || '—'}</td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  )
}
