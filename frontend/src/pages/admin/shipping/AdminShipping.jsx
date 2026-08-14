import { Fragment, useCallback, useEffect, useState } from 'react'
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

function shippingPill(status, awb, labelUrl) {
  if (labelUrl) return 'admin-pill on'
  if (awb) return 'admin-pill'
  if ((status || '').toLowerCase() === 'readytoship') return 'admin-pill'
  return 'admin-pill off'
}

function shipmentMatchesTab(shipment, tab) {
  const sellerReady = !!(shipment.sellerReady || shipment.sellerReadyToShipAt)
  if (tab === 'labeled') return !!shipment.labelUrl
  if (tab === 'label' || tab === 'awb') return !!shipment.awbCode && !shipment.labelUrl
  if (tab === 'assign-awb' || tab === 'assign') return !!shipment.readyToShipAt && !shipment.awbCode
  if (tab === 'ready') return sellerReady && !shipment.readyToShipAt && !shipment.awbCode
  return !!shipment.shiprocketShipmentId && !shipment.readyToShipAt && !shipment.awbCode && !sellerReady
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

const ORDER_GUID_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i

function logsQueryParams(orderQuery, shipmentId) {
  const params = { take: 40 }
  if (shipmentId) {
    params.shipmentId = shipmentId
    return params
  }
  const q = (orderQuery || '').trim()
  if (!q) return null
  if (ORDER_GUID_RE.test(q)) params.orderId = q
  else params.orderNumber = q
  return params
}

export default function AdminShipping() {
  const [tab, setTab] = useState('new')
  const [result, setResult] = useState({
    items: [],
    totalCount: 0,
    tab: 'new',
    newCount: 0,
    readyCount: 0,
    assignAwbCount: 0,
    labelCount: 0,
    labeledCount: 0,
  })
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [actionError, setActionError] = useState('')
  const [busyShipmentId, setBusyShipmentId] = useState('')
  const [couriersByShipment, setCouriersByShipment] = useState({})
  const [courierMetaByShipment, setCourierMetaByShipment] = useState({})
  const [courierLoadingId, setCourierLoadingId] = useState('')
  const [courierErrorByShipment, setCourierErrorByShipment] = useState({})
  const [apiLogs, setApiLogs] = useState([])
  const [logsLoading, setLogsLoading] = useState(false)
  const [logsError, setLogsError] = useState('')
  const [expandedLogId, setExpandedLogId] = useState(null)
  const [logsFilterShipmentId, setLogsFilterShipmentId] = useState('')
  const [logsSearchInput, setLogsSearchInput] = useState('')
  const [logsOrderQuery, setLogsOrderQuery] = useState('')
  const [logsActive, setLogsActive] = useState(false)

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
        assignAwbCount: data.assignAwbCount || 0,
        labelCount: data.labelCount ?? data.awbCount ?? 0,
        labeledCount: data.labeledCount || 0,
      })
    } catch (err) {
      setError(err.message || 'Unable to load shipping orders.')
      setResult((prev) => ({ ...prev, items: [], totalCount: 0 }))
    } finally {
      setLoading(false)
    }
  }, [tab])

  const loadLogs = useCallback(async (orderQuery, shipmentId) => {
    const params = logsQueryParams(orderQuery, shipmentId)
    if (!params) {
      setApiLogs([])
      setLogsError('')
      setLogsActive(false)
      return
    }

    setLogsActive(true)
    setLogsLoading(true)
    setLogsError('')
    try {
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
    if (logsFilterShipmentId || logsOrderQuery) {
      loadLogs(logsOrderQuery, logsFilterShipmentId)
    } else {
      setApiLogs([])
      setLogsActive(false)
      setLogsError('')
    }
  }, [loadLogs, logsFilterShipmentId, logsOrderQuery])

  const searchLogs = (event) => {
    event?.preventDefault?.()
    const q = logsSearchInput.trim()
    if (!q) {
      setLogsOrderQuery('')
      setLogsFilterShipmentId('')
      setExpandedLogId(null)
      setApiLogs([])
      setLogsActive(false)
      setLogsError('')
      return
    }
    setLogsFilterShipmentId('')
    setExpandedLogId(null)
    setLogsOrderQuery(q)
  }

  const clearLogsSearch = () => {
    setLogsSearchInput('')
    setLogsOrderQuery('')
    setLogsFilterShipmentId('')
    setExpandedLogId(null)
    setApiLogs([])
    setLogsActive(false)
    setLogsError('')
  }

  const readyToShip = async (shipment, { switchToAssignTab = false } = {}) => {
    setBusyShipmentId(shipment.id)
    setCourierLoadingId(shipment.id)
    setActionError('')
    setCourierErrorByShipment((prev) => {
      const next = { ...prev }
      delete next[shipment.id]
      return next
    })
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
      if (switchToAssignTab) {
        setTab('assign-awb')
      } else {
        await load()
      }
      if (logsFilterShipmentId || logsOrderQuery) {
        await loadLogs(logsOrderQuery, logsFilterShipmentId)
      }
    } catch (err) {
      const message = err.message || 'Ready to Ship failed.'
      setActionError(message)
      setCourierErrorByShipment((prev) => ({ ...prev, [shipment.id]: message }))
    } finally {
      setBusyShipmentId('')
      setCourierLoadingId('')
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
      setCourierMetaByShipment((prev) => {
        const next = { ...prev }
        delete next[shipment.id]
        return next
      })
      setCourierErrorByShipment((prev) => {
        const next = { ...prev }
        delete next[shipment.id]
        return next
      })
      await load()
      if (logsFilterShipmentId || logsOrderQuery) {
        await loadLogs(logsOrderQuery, logsFilterShipmentId)
      }
    } catch (err) {
      setActionError(err.message || 'Assign AWB failed.')
    } finally {
      setBusyShipmentId('')
    }
  }

  const generateLabel = async (shipment) => {
    setBusyShipmentId(shipment.id)
    setActionError('')
    try {
      await api.adminShippingGenerateLabel(shipment.id)
      await load()
      if (logsFilterShipmentId || logsOrderQuery) {
        await loadLogs(logsOrderQuery, logsFilterShipmentId)
      }
    } catch (err) {
      setActionError(err.message || 'Generate Label failed.')
    } finally {
      setBusyShipmentId('')
    }
  }

  const tabs = [
    { id: 'new', label: 'New', count: result.newCount },
    { id: 'ready', label: 'Ready to Ship', count: result.readyCount },
    { id: 'assign-awb', label: 'Assign AWB', count: result.assignAwbCount },
    { id: 'label', label: 'Generate Label', count: result.labelCount },
    { id: 'labeled', label: 'Label Generated', count: result.labeledCount },
  ]

  const rows = []
  for (const order of result.items) {
    for (const shipment of order.shipments || []) {
      if (!shipmentMatchesTab(shipment, tab)) continue
      rows.push({ order, shipment, highlight: true })
    }
  }

  return (
    <div className="admin-page admin-shipping">
      <div className="admin-page-head">
        <div>
          <p className="eyebrow">Fulfillment</p>
          <h1>Shipping</h1>
          <p className="admin-subtitle">
            Per-pickup Shiprocket shipments. Ready to Ship → Assign AWB → Generate Label → download for sellers.
          </p>
        </div>
        <button type="button" className="btn btn-secondary btn-sm" onClick={load} disabled={loading}>
          {loading ? 'Refreshing…' : 'Refresh'}
        </button>
      </div>

      {error ? <p className="admin-error">{error}</p> : null}
      {actionError ? <p className="admin-error">{actionError}</p> : null}

      <div className="admin-tabs admin-shipping-tabs" role="tablist">
        {tabs.map((t) => (
          <button
            key={t.id}
            type="button"
            role="tab"
            aria-selected={tab === t.id}
            className={`admin-tab${tab === t.id ? ' active' : ''}`}
            onClick={() => {
              setTab(t.id)
              setActionError('')
            }}
          >
            {t.label}
            <span className="admin-shipping-tab-count">{t.count}</span>
          </button>
        ))}
      </div>

      {loading && !rows.length ? (
        <p className="admin-muted">Loading shipping orders…</p>
      ) : !rows.length ? (
        <p className="admin-muted">No orders in this tab. Confirmed orders need a successful Shiprocket create first.</p>
      ) : (
        <div className="admin-table-wrap admin-shipping-table-wrap">
          <table className="admin-table admin-shipping-table">
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
                const couriersLoaded = Object.prototype.hasOwnProperty.call(
                  couriersByShipment,
                  shipment.id,
                )
                const couriers = couriersByShipment[shipment.id] || []
                const meta = courierMetaByShipment[shipment.id]
                const courierError = courierErrorByShipment[shipment.id]
                const courierLoading = courierLoadingId === shipment.id
                const busy = busyShipmentId === shipment.id
                const sellerReady = !!(shipment.sellerReady || shipment.sellerReadyToShipAt)
                const canReady =
                  !!shipment.shiprocketShipmentId &&
                  !shipment.awbCode &&
                  sellerReady &&
                  !shipment.readyToShipAt
                const canRefreshCouriers =
                  !!shipment.shiprocketShipmentId && !shipment.awbCode && !!shipment.readyToShipAt
                const waitingForSeller =
                  !!shipment.shiprocketShipmentId && !shipment.awbCode && !sellerReady
                const canGenerateLabel = !!shipment.awbCode && !shipment.labelUrl
                const showCourierPanel =
                  canRefreshCouriers && (couriersLoaded || courierLoading || !!courierError)

                return (
                  <Fragment key={shipment.id}>
                    <tr className={highlight ? undefined : 'admin-shipping-row-dim'}>
                      <td>
                        <strong className="admin-shipping-order-num">{order.orderNumber}</strong>
                        <div className="admin-muted admin-shipping-meta">{formatDateTime(order.createdAt)}</div>
                      </td>
                      <td>
                        <div>{order.customerName}</div>
                        <div className="admin-muted admin-shipping-meta">{order.email}</div>
                        <div className="admin-muted admin-shipping-meta">PIN {order.zip || '—'}</div>
                      </td>
                      <td>
                        <div>{order.paymentProvider || '—'}</div>
                        <div className="admin-muted admin-shipping-meta">{formatPrice(order.total, order.currency)}</div>
                      </td>
                      <td>
                        <strong>{shipment.pickupLocation}</strong>
                        <div className="admin-muted admin-shipping-meta">SR ship #{shipment.shiprocketShipmentId || '—'}</div>
                        <div className="admin-muted admin-shipping-meta">SR order {shipment.shiprocketOrderId || '—'}</div>
                        {waitingForSeller ? (
                          <div className="admin-muted admin-shipping-meta">Waiting for seller</div>
                        ) : sellerReady && !shipment.awbCode ? (
                          <div className="admin-muted admin-shipping-meta">Seller ready</div>
                        ) : null}
                      </td>
                      <td>
                        <span
                          className={shippingPill(
                            shipment.shippingStatus,
                            shipment.awbCode,
                            shipment.labelUrl,
                          )}
                        >
                          {shipment.labelUrl
                            ? 'Label Generated'
                            : shipment.awbCode
                              ? 'Generate Label'
                              : shipment.shippingStatus || shipment.status || 'Created'}
                        </span>
                        {shipment.lastError ? (
                          <div className="admin-error admin-shipping-error">{shipment.lastError}</div>
                        ) : null}
                      </td>
                      <td>
                        {shipment.awbCode ? (
                          <>
                            <strong>{shipment.awbCode}</strong>
                            <div className="admin-muted admin-shipping-meta">
                              {shipment.courierName || 'Courier'}
                              {shipment.courierId ? ` (#${shipment.courierId})` : ''}
                            </div>
                            <div>
                              {shipment.actualShippingCharge != null
                                ? formatShippingPrice(shipment.actualShippingCharge)
                                : '—'}
                            </div>
                            {shipment.labelUrl ? (
                              <div className="admin-muted admin-shipping-meta">
                                <a
                                  href={shipment.labelUrl}
                                  target="_blank"
                                  rel="noopener noreferrer"
                                >
                                  Open label
                                </a>
                              </div>
                            ) : null}
                          </>
                        ) : (
                          <span className="admin-muted">—</span>
                        )}
                      </td>
                      <td className="admin-shipping-actions">
                        {canReady ? (
                          <button
                            type="button"
                            className="btn btn-primary btn-sm"
                            disabled={busy}
                            onClick={() => readyToShip(shipment, { switchToAssignTab: true })}
                          >
                            {courierLoading ? 'Loading…' : 'Ready to Ship'}
                          </button>
                        ) : canRefreshCouriers ? (
                          <button
                            type="button"
                            className="btn btn-secondary btn-sm"
                            disabled={busy}
                            onClick={() => readyToShip(shipment)}
                          >
                            {courierLoading ? 'Loading…' : 'Refresh Couriers'}
                          </button>
                        ) : waitingForSeller ? (
                          <button
                            type="button"
                            className="btn btn-secondary btn-sm"
                            disabled
                            title="Waiting for seller to mark Ready to Ship"
                          >
                            Waiting for seller
                          </button>
                        ) : null}
                        {canGenerateLabel ? (
                          <button
                            type="button"
                            className="btn btn-primary btn-sm"
                            disabled={busy}
                            onClick={() => generateLabel(shipment)}
                          >
                            {busy ? 'Generating…' : 'Generate Label'}
                          </button>
                        ) : null}
                        <button
                          type="button"
                          className="btn btn-secondary btn-sm"
                          onClick={() => {
                            setLogsSearchInput(order.orderNumber || order.id || '')
                            setLogsOrderQuery('')
                            setLogsFilterShipmentId(shipment.id)
                            setExpandedLogId(null)
                          }}
                        >
                          API logs
                        </button>
                      </td>
                    </tr>
                    {showCourierPanel ? (
                      <tr className="admin-shipping-courier-row">
                        <td colSpan={7}>
                          <div className="admin-shipping-couriers">
                            <div className="admin-shipping-couriers-head">
                              <div className="admin-shipping-couriers-title-wrap">
                                <span className="admin-shipping-couriers-title">Couriers</span>
                                {courierLoading ? (
                                  <span className="admin-muted admin-shipping-couriers-status">Refreshing…</span>
                                ) : null}
                              </div>
                              {meta ? (
                                <p className="admin-muted admin-shipping-courier-meta">
                                  {meta.pickupPostcode} → {meta.deliveryPostcode} · {meta.weightKg} kg ·{' '}
                                  {meta.length}×{meta.breadth}×{meta.height} cm · decl{' '}
                                  {formatPrice(meta.declaredValue, order.currency)} ·{' '}
                                  {meta.cod ? 'COD' : 'Prepaid'}
                                </p>
                              ) : null}
                            </div>

                            {courierLoading && !couriersLoaded ? (
                              <p className="admin-shipping-couriers-state">Loading couriers…</p>
                            ) : courierError && !couriersLoaded ? (
                              <p className="admin-error admin-shipping-couriers-state">{courierError}</p>
                            ) : couriersLoaded && couriers.length === 0 ? (
                              <p className="admin-shipping-couriers-state">
                                No couriers available for this route and package.
                              </p>
                            ) : (
                              <div className="admin-table-wrap admin-shipping-courier-wrap">
                                <table className="admin-table admin-shipping-courier-table">
                                  <thead>
                                    <tr>
                                      <th>Name</th>
                                      <th>Rating</th>
                                      <th>Rate</th>
                                      <th>Expected pickup</th>
                                      <th>Delivery ETA</th>
                                      <th className="admin-shipping-courier-awb-col">Assign AWB</th>
                                    </tr>
                                  </thead>
                                  <tbody>
                                    {couriers.map((c) => {
                                      const breakdown = courierRateBreakdown(c)
                                      const ratingNum = Number(c.rating)
                                      return (
                                        <tr key={c.courierId}>
                                          <td className="admin-shipping-courier-name">{c.courierName}</td>
                                          <td className="admin-shipping-courier-rating">
                                            {Number.isFinite(ratingNum) && ratingNum > 0
                                              ? ratingNum.toFixed(1)
                                              : '—'}
                                          </td>
                                          <td className="admin-shipping-courier-rate">
                                            <span className="admin-shipping-courier-rate-value">
                                              {formatShippingPrice(c.rate)}
                                            </span>
                                            {breakdown ? (
                                              <div
                                                className="admin-muted admin-shipping-rate-breakdown"
                                                title={breakdown}
                                              >
                                                {breakdown}
                                              </div>
                                            ) : null}
                                          </td>
                                          <td>{c.expectedPickup || '—'}</td>
                                          <td>
                                            {c.estimatedDelivery ||
                                              (c.estimatedDeliveryDays != null
                                                ? `${c.estimatedDeliveryDays} days`
                                                : '—')}
                                          </td>
                                          <td className="admin-shipping-courier-awb-col">
                                            <button
                                              type="button"
                                              className="btn btn-secondary btn-sm admin-shipping-awb-btn"
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
                            )}
                          </div>
                        </td>
                      </tr>
                    ) : null}
                  </Fragment>
                )
              })}
            </tbody>
          </table>
        </div>
      )}

      <section className="admin-shipping-logs">
        <div className="admin-page-head admin-shipping-logs-head">
          <div>
            <h2>Shiprocket API logs</h2>
            <p className="admin-muted">
              Exact request bodies / query strings sent to Shiprocket (tokens and passwords redacted). Search by
              Bagly order number or order Guid.
              {logsFilterShipmentId ? ' Showing one shipment.' : null}
            </p>
          </div>
          <div className="admin-shipping-logs-actions">
            {logsActive ? (
              <button type="button" className="btn btn-secondary btn-sm" onClick={clearLogsSearch}>
                Clear search
              </button>
            ) : null}
            {logsActive ? (
              <button
                type="button"
                className="btn btn-secondary btn-sm"
                disabled={logsLoading}
                onClick={() => loadLogs(logsOrderQuery, logsFilterShipmentId)}
              >
                {logsLoading ? 'Loading…' : 'Refresh logs'}
              </button>
            ) : null}
          </div>
        </div>

        <form className="admin-shipping-logs-search" onSubmit={searchLogs}>
          <input
            type="search"
            value={logsSearchInput}
            onChange={(e) => setLogsSearchInput(e.target.value)}
            placeholder="Search by order ID / order number"
            aria-label="Search Shiprocket API logs by order ID or order number"
          />
          <button type="submit" className="btn btn-primary btn-sm" disabled={logsLoading}>
            Search
          </button>
        </form>

        {logsError ? <p className="admin-error">{logsError}</p> : null}
        {!logsActive ? (
          <p className="admin-muted">Enter order ID to view logs</p>
        ) : logsLoading && !apiLogs.length ? (
          <p className="admin-muted">Loading API logs…</p>
        ) : !apiLogs.length ? (
          <p className="admin-muted">No Shiprocket API logs found for this order.</p>
        ) : (
          <div className="admin-table-wrap admin-shipping-table-wrap">
            <table className="admin-table admin-shipping-table admin-shipping-logs-table">
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
                        <div className="admin-muted admin-shipping-meta">#{log.id}</div>
                      </td>
                      <td>
                        <strong>{log.action}</strong>
                        <div className="admin-muted admin-shipping-meta">{truncate(log.url, 48)}</div>
                      </td>
                      <td>{log.httpMethod}</td>
                      <td>{log.responseStatus ?? '—'}</td>
                      <td className="admin-shipping-log-request">
                        <button
                          type="button"
                          className="btn btn-secondary btn-sm"
                          onClick={() => setExpandedLogId(open ? null : log.id)}
                        >
                          {open ? 'Hide request' : 'View request'}
                        </button>
                        {open ? (
                          <pre className="admin-shipping-log-pre">
                            {log.requestJson || '(empty)'}
                            {log.responseJson ? `\n\n--- response ---\n${log.responseJson}` : ''}
                          </pre>
                        ) : (
                          <div className="admin-muted admin-shipping-meta">{truncate(log.requestJson, 100)}</div>
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
