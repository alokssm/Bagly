import { useCallback, useEffect, useRef, useState } from 'react'
import { api } from '../api/client'
import SellerHubNav from '../components/SellerHubNav'
import { useSellerAuth } from '../context/SellerAuthContext'
import { formatPrice } from '../utils/format'

const PAGE_SIZE = 20
const emptyMeta = {
  ownedProductCount: 0,
  registeredPickupCount: 0,
  visibleProductCount: 0,
}
const money = (value) => formatPrice(value, { fractionDigits: 2 })

function formatWhen(value) {
  if (!value) return '—'
  try {
    return new Date(value).toLocaleString(undefined, {
      day: 'numeric',
      month: 'short',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    })
  } catch {
    return String(value)
  }
}

function shipmentLabel(shipment) {
  const tracking = formatTrackingStatus(shipment.trackingStatus)
  if (tracking) {
    const awb = shipment.awbCode ? `AWB ${shipment.awbCode}` : null
    return awb ? `${awb} · ${tracking}` : tracking
  }
  if (shipment.manifestUrl || shipment.canDownloadManifest) {
    const awb = shipment.awbCode ? `AWB ${shipment.awbCode}` : null
    return awb ? `${awb} · Manifest ready` : 'Manifest ready'
  }
  if (shipment.pickupRequestedAt) return shipment.awbCode ? `AWB ${shipment.awbCode} · Pickup requested` : 'Pickup requested'
  if (shipment.labelUrl || shipment.canDownloadLabel) return `AWB ${shipment.awbCode || '—'}`
  if (shipment.awbCode) return `AWB ${shipment.awbCode}`
  if (shipment.shippingStatus === 'Cancelled' || shipment.status === 'Cancelled') return 'Cancelled'
  if (shipment.sellerReady) return 'Seller ready'
  if (shipment.shiprocketShipmentId) return 'Awaiting ready'
  return 'Pending Shiprocket'
}

function formatTrackingStatus(status) {
  if (!status) return null
  const map = {
    PICKUP_REQUESTED: 'Pickup requested',
    PICKED_UP: 'Picked up',
    IN_TRANSIT: 'In transit',
    OUT_FOR_DELIVERY: 'Out for delivery',
    DELIVERED: 'Delivered',
  }
  return map[status] || String(status).replaceAll('_', ' ')
}

function canDownloadLabel(shipment) {
  return !!(shipment.canDownloadLabel || shipment.labelUrl)
}

function canDownloadManifest(shipment) {
  return !!(shipment.canDownloadManifest || shipment.manifestUrl)
}

function lineTotalOf(item) {
  if (Number.isFinite(Number(item.lineTotal))) return Number(item.lineTotal)
  return (Number(item.unitPrice) || 0) * (Number(item.quantity) || 0)
}

export default function SellerOrders() {
  const { user, logout } = useSellerAuth()
  const approved = (user?.status || '').toLowerCase() === 'approved'
  // Filter state — reset list when these change (UI may be added later).
  const [filters] = useState({ status: '', from: '', to: '' })
  const [orders, setOrders] = useState([])
  const [page, setPage] = useState(1)
  const [hasMore, setHasMore] = useState(false)
  const [meta, setMeta] = useState(emptyMeta)
  const [loading, setLoading] = useState(approved)
  const [loadingMore, setLoadingMore] = useState(false)
  const [error, setError] = useState('')
  const [busyKey, setBusyKey] = useState('')
  const [openMenuId, setOpenMenuId] = useState('')
  const menuRef = useRef(null)
  const loadMoreRef = useRef(null)
  const loadingMoreRef = useRef(false)

  const listParams = useCallback(
    (pageNum) => ({
      page: pageNum,
      pageSize: PAGE_SIZE,
      status: filters.status || undefined,
      from: filters.from || undefined,
      to: filters.to || undefined,
    }),
    [filters.status, filters.from, filters.to],
  )

  const applyListResult = useCallback((data, { append }) => {
    const items = Array.isArray(data?.items) ? data.items : []
    const totalPages = Number(data?.totalPages) || 0
    const currentPage = Number(data?.page) || 1
    setMeta({
      ownedProductCount: data?.ownedProductCount ?? 0,
      registeredPickupCount: data?.registeredPickupCount ?? 0,
      visibleProductCount: data?.visibleProductCount ?? 0,
    })
    if (append) {
      setOrders((prev) => {
        const seen = new Set(prev.map((o) => o.id))
        const appended = items.filter((o) => !seen.has(o.id))
        return appended.length ? [...prev, ...appended] : prev
      })
    } else {
      setOrders(items)
    }
    setPage(currentPage)
    setHasMore(currentPage < totalPages)
  }, [])

  // Reset to first page when filters change.
  useEffect(() => {
    if (!approved) {
      setLoading(false)
      setOrders([])
      setHasMore(false)
      setMeta(emptyMeta)
      return undefined
    }

    let cancelled = false

    async function loadFirstPage() {
      setLoading(true)
      setError('')
      setOrders([])
      setPage(1)
      setHasMore(false)
      try {
        const data = await api.sellerGetOrders(listParams(1))
        if (cancelled) return
        applyListResult(data, { append: false })
      } catch (err) {
        if (cancelled) return
        setOrders([])
        setHasMore(false)
        setMeta(emptyMeta)
        setError(err.message || 'Unable to load orders.')
      } finally {
        if (!cancelled) setLoading(false)
      }
    }

    loadFirstPage()
    return () => {
      cancelled = true
    }
  }, [approved, listParams, applyListResult])

  const loadMore = useCallback(async () => {
    if (!approved || loading || loadingMoreRef.current || !hasMore || error) return
    loadingMoreRef.current = true
    setLoadingMore(true)
    const nextPage = page + 1
    try {
      const data = await api.sellerGetOrders(listParams(nextPage))
      applyListResult(data, { append: true })
    } catch (err) {
      setError(err.message || 'Unable to load more orders.')
      setHasMore(false)
    } finally {
      loadingMoreRef.current = false
      setLoadingMore(false)
    }
  }, [approved, loading, hasMore, error, page, listParams, applyListResult])

  useEffect(() => {
    const node = loadMoreRef.current
    if (!node || loading || !hasMore) return undefined

    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((entry) => entry.isIntersecting)) {
          loadMore()
        }
      },
      { root: null, rootMargin: '240px 0px', threshold: 0 },
    )
    observer.observe(node)
    return () => observer.disconnect()
  }, [loading, hasMore, loadMore, orders.length])

  useEffect(() => {
    const onDocClick = (event) => {
      if (!menuRef.current) return
      if (!menuRef.current.contains(event.target)) setOpenMenuId('')
    }
    document.addEventListener('mousedown', onDocClick)
    return () => document.removeEventListener('mousedown', onDocClick)
  }, [])

  const reloadFirstPage = async () => {
    setLoading(true)
    setError('')
    try {
      const data = await api.sellerGetOrders(listParams(1))
      applyListResult(data, { append: false })
    } catch (err) {
      setError(err.message || 'Unable to load orders.')
      setOrders([])
      setHasMore(false)
    } finally {
      setLoading(false)
    }
  }

  const markReady = async (order, shipment) => {
    const key = `${order.id}:${shipment.id}:ready`
    setBusyKey(key)
    setError('')
    try {
      await api.sellerMarkShipmentReadyToShip(order.id, shipment.id)
      await reloadFirstPage()
    } catch (err) {
      setError(err.message || 'Could not mark ready to ship.')
    } finally {
      setBusyKey('')
    }
  }

  const cancelOrder = async (order) => {
    setOpenMenuId('')
    if (
      !window.confirm(
        `Cancel your items on order ${order.orderNumber}? This cannot be undone once confirmed.`,
      )
    ) {
      return
    }
    const key = `${order.id}:cancel`
    setBusyKey(key)
    setError('')
    try {
      await api.sellerCancelOrder(order.id)
      await reloadFirstPage()
    } catch (err) {
      setError(err.message || 'Could not cancel order.')
    } finally {
      setBusyKey('')
    }
  }

  return (
    <div className="seller-page">
      <div className="seller-shell seller-shell--wide">
        <header className="seller-head">
          <div>
            <p className="eyebrow">Seller hub</p>
            <h1>Orders</h1>
            <p className="seller-lead">
              Orders with your products. Mark Ready to Ship when packed; cancel only before AWB.
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
            <span>Orders are locked until your seller account is approved.</span>
          </div>
        ) : (
          <>
            {error ? (
              <p className="form-error" role="alert">
                {error}
              </p>
            ) : null}

            {loading && !orders.length ? (
              <p className="seller-lead">Loading orders…</p>
            ) : !orders.length ? (
              <div className="seller-status" role="status">
                <strong>No orders yet</strong>
                <span>
                  Orders show when customers buy your products, or platform products fulfilled from your
                  pickup nicknames (e.g. wareHouse1). You have {meta.ownedProductCount ?? 0} product
                  {(meta.ownedProductCount || 0) === 1 ? '' : 's'}, {meta.registeredPickupCount ?? 0}{' '}
                  pickup{(meta.registeredPickupCount || 0) === 1 ? '' : 's'}, and{' '}
                  {meta.visibleProductCount ?? 0} visible catalog match
                  {(meta.visibleProductCount || 0) === 1 ? '' : 'es'}.
                </span>
              </div>
            ) : (
              <>
              <div className="seller-orders-list" ref={menuRef}>
                {orders.map((order) => {
                  const cancelled =
                    (order.status || '').toLowerCase() === 'cancelled' ||
                    (order.shipments || []).every(
                      (s) =>
                        (s.status || '').toLowerCase() === 'cancelled' ||
                        (s.shippingStatus || '').toLowerCase() === 'cancelled',
                    )
                  const canCancel =
                    !cancelled &&
                    !(order.shipments || []).some((s) => s.awbCode || s.readyToShipAt)
                  const actionableShipments = (order.shipments || []).filter((s) => {
                    const shipCancelled =
                      (s.status || '').toLowerCase() === 'cancelled' ||
                      (s.shippingStatus || '').toLowerCase() === 'cancelled'
                    return !!s.shiprocketShipmentId && !s.awbCode && !s.sellerReady && !shipCancelled
                  })
                  const cancelBusy = busyKey === `${order.id}:cancel`
                  const menuOpen = openMenuId === order.id
                  const yourSubtotal = Number.isFinite(Number(order.subtotal))
                    ? Number(order.subtotal)
                    : Number(order.sellerSubtotal) || 0
                  const orderTotal = Number(order.orderTotal)
                  const showOrderTotal =
                    Number.isFinite(orderTotal) && Math.abs(orderTotal - yourSubtotal) > 0.005

                  return (
                    <article key={order.id} className="seller-order-card">
                      <div className="seller-order-main">
                        <div className="seller-order-top">
                          <strong className="seller-order-num">{order.orderNumber}</strong>
                          <span className="seller-order-status">{order.status}</span>
                        </div>
                        <div className="seller-order-meta">
                          {formatWhen(order.createdAt)} · {order.customerName}
                          {order.city ? ` · ${order.city}` : ''}
                          {order.zip ? ` ${order.zip}` : ''}
                        </div>
                        <ul className="seller-order-items">
                          {(order.items || []).map((item) => (
                            <li key={`${order.id}-${item.productId}-${item.color}`}>
                              <span>
                                {item.productName}
                                {item.color ? ` · ${item.color}` : ''}
                              </span>
                              <span>
                                {money(item.unitPrice)} × {item.quantity} = {money(lineTotalOf(item))}
                              </span>
                            </li>
                          ))}
                        </ul>
                        <div className="seller-order-shipments">
                          {(order.shipments || []).length === 0 ? (
                            <span className="seller-order-hint">No Shiprocket shipment yet for your pickup.</span>
                          ) : (
                            (order.shipments || []).map((s) => (
                              <div key={s.id} className="seller-order-ship-row">
                                <strong>{s.pickupLocation}</strong>
                                <span>{shipmentLabel(s)}</span>
                                {canDownloadLabel(s) ? (
                                  <a
                                    className="seller-order-label-link"
                                    href={s.labelUrl}
                                    target="_blank"
                                    rel="noopener noreferrer"
                                  >
                                    Download Label
                                  </a>
                                ) : null}
                                {canDownloadManifest(s) ? (
                                  <a
                                    className="seller-order-label-link"
                                    href={s.manifestUrl}
                                    target="_blank"
                                    rel="noopener noreferrer"
                                  >
                                    Download Manifest
                                  </a>
                                ) : null}
                              </div>
                            ))
                          )}
                        </div>
                        <div className="seller-order-total">
                          <div>Your items subtotal: {money(yourSubtotal)}</div>
                          {showOrderTotal ? (
                            <div className="seller-order-total-note">
                              Order total: {money(orderTotal)}
                              {Number.isFinite(Number(order.shipping)) && Number(order.shipping) > 0
                                ? ` (incl. shipping ${money(order.shipping)})`
                                : ''}
                            </div>
                          ) : null}
                        </div>
                      </div>

                      <div className="seller-order-actions">
                        <div className="seller-order-action-stack">
                          {actionableShipments.map((s) => {
                            const rowBusy = busyKey === `${order.id}:${s.id}:ready`
                            return (
                              <button
                                key={s.id}
                                type="button"
                                className="btn btn-primary btn-sm"
                                disabled={rowBusy || cancelBusy}
                                title={
                                  actionableShipments.length > 1
                                    ? `Ready to Ship · ${s.pickupLocation}`
                                    : undefined
                                }
                                onClick={() => markReady(order, s)}
                              >
                                {rowBusy ? 'Saving…' : 'Ready to Ship'}
                              </button>
                            )
                          })}
                          {(order.shipments || [])
                            .filter((s) => canDownloadLabel(s))
                            .map((s) => (
                              <a
                                key={`label-${s.id}`}
                                className="btn btn-secondary btn-sm"
                                href={s.labelUrl}
                                target="_blank"
                                rel="noopener noreferrer"
                                title={
                                  (order.shipments || []).filter((x) => canDownloadLabel(x)).length > 1
                                    ? `Download Label · ${s.pickupLocation}`
                                    : undefined
                                }
                              >
                                Download Label
                              </a>
                            ))}
                          {(order.shipments || [])
                            .filter((s) => canDownloadManifest(s))
                            .map((s) => (
                              <a
                                key={`manifest-${s.id}`}
                                className="btn btn-secondary btn-sm"
                                href={s.manifestUrl}
                                target="_blank"
                                rel="noopener noreferrer"
                                title={
                                  (order.shipments || []).filter((x) => canDownloadManifest(x)).length > 1
                                    ? `Download Manifest · ${s.pickupLocation}`
                                    : undefined
                                }
                              >
                                Download Manifest
                              </a>
                            ))}
                          {actionableShipments.length === 0 &&
                          (order.shipments || []).some((s) => s.sellerReady && !s.awbCode) ? (
                            <span className="seller-order-hint">Marked ready</span>
                          ) : null}
                          {actionableShipments.length === 0 && cancelled ? (
                            <span className="seller-order-hint">Cancelled</span>
                          ) : null}
                          {actionableShipments.length === 0 &&
                          !cancelled &&
                          !(order.shipments || []).some((s) => s.sellerReady) ? (
                            <span className="seller-order-hint">Waiting for shipment</span>
                          ) : null}
                        </div>

                        <div className="seller-order-menu">
                          <button
                            type="button"
                            className="seller-order-kebab"
                            aria-haspopup="menu"
                            aria-expanded={menuOpen}
                            aria-label={`Order ${order.orderNumber} actions`}
                            disabled={cancelBusy}
                            onClick={() => setOpenMenuId(menuOpen ? '' : order.id)}
                          >
                            ⋮
                          </button>
                          {menuOpen ? (
                            <div className="seller-order-menu-panel" role="menu">
                              <button
                                type="button"
                                role="menuitem"
                                disabled={!canCancel || cancelBusy}
                                title={
                                  canCancel
                                    ? undefined
                                    : 'Cancel is only available before admin Ready to Ship / AWB'
                                }
                                onClick={() => cancelOrder(order)}
                              >
                                {cancelBusy ? 'Cancelling…' : 'Cancel Order'}
                              </button>
                            </div>
                          ) : null}
                        </div>
                      </div>
                    </article>
                  )
                })}
              </div>
              <div
                ref={loadMoreRef}
                className="seller-orders-load-more"
                aria-hidden={!hasMore && !loadingMore}
              >
                {loadingMore ? (
                  <p className="seller-lead" role="status">
                    Loading…
                  </p>
                ) : null}
              </div>
              </>
            )}
          </>
        )}
      </div>
    </div>
  )
}
