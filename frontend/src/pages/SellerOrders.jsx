import { useCallback, useEffect, useRef, useState } from 'react'
import { api } from '../api/client'
import SellerHubNav from '../components/SellerHubNav'
import { useSellerAuth } from '../context/SellerAuthContext'
import { formatPrice } from '../utils/format'

const PAGE_SIZE = 50
const emptyResult = {
  items: [],
  totalCount: 0,
  totalPages: 0,
  page: 1,
  pageSize: PAGE_SIZE,
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
  if (shipment.awbCode) return `AWB ${shipment.awbCode}`
  if (shipment.shippingStatus === 'Cancelled' || shipment.status === 'Cancelled') return 'Cancelled'
  if (shipment.sellerReady) return 'Seller ready'
  if (shipment.shiprocketShipmentId) return 'Awaiting ready'
  return 'Pending Shiprocket'
}

function lineTotalOf(item) {
  if (Number.isFinite(Number(item.lineTotal))) return Number(item.lineTotal)
  return (Number(item.unitPrice) || 0) * (Number(item.quantity) || 0)
}

export default function SellerOrders() {
  const { user, logout } = useSellerAuth()
  const approved = (user?.status || '').toLowerCase() === 'approved'
  const [page, setPage] = useState(1)
  const [result, setResult] = useState(emptyResult)
  const [loading, setLoading] = useState(approved)
  const [error, setError] = useState('')
  const [busyKey, setBusyKey] = useState('')
  const [openMenuId, setOpenMenuId] = useState('')
  const menuRef = useRef(null)

  const load = useCallback(async () => {
    if (!approved) {
      setLoading(false)
      return
    }
    setLoading(true)
    setError('')
    try {
      const data = await api.sellerGetOrders({ page, pageSize: PAGE_SIZE })
      setResult({
        items: data.items || [],
        totalCount: data.totalCount || 0,
        totalPages: data.totalPages || 0,
        page: data.page || page,
        pageSize: data.pageSize || PAGE_SIZE,
        ownedProductCount: data.ownedProductCount ?? 0,
        registeredPickupCount: data.registeredPickupCount ?? 0,
        visibleProductCount: data.visibleProductCount ?? 0,
      })
    } catch (err) {
      setError(err.message || 'Unable to load orders.')
      setResult(emptyResult)
    } finally {
      setLoading(false)
    }
  }, [approved, page])

  useEffect(() => {
    load()
  }, [load])

  useEffect(() => {
    const onDocClick = (event) => {
      if (!menuRef.current) return
      if (!menuRef.current.contains(event.target)) setOpenMenuId('')
    }
    document.addEventListener('mousedown', onDocClick)
    return () => document.removeEventListener('mousedown', onDocClick)
  }, [])

  const markReady = async (order, shipment) => {
    const key = `${order.id}:${shipment.id}:ready`
    setBusyKey(key)
    setError('')
    try {
      await api.sellerMarkShipmentReadyToShip(order.id, shipment.id)
      await load()
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
      await load()
    } catch (err) {
      setError(err.message || 'Could not cancel order.')
    } finally {
      setBusyKey('')
    }
  }

  const { items: orders, totalCount, totalPages } = result
  const from = totalCount === 0 ? 0 : (result.page - 1) * result.pageSize + 1
  const to = Math.min(result.page * result.pageSize, totalCount)

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
                  pickup nicknames (e.g. wareHouse1). You have {result.ownedProductCount ?? 0} product
                  {(result.ownedProductCount || 0) === 1 ? '' : 's'}, {result.registeredPickupCount ?? 0}{' '}
                  pickup{(result.registeredPickupCount || 0) === 1 ? '' : 's'}, and{' '}
                  {result.visibleProductCount ?? 0} visible catalog match
                  {(result.visibleProductCount || 0) === 1 ? '' : 'es'}.
                </span>
              </div>
            ) : (
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
            )}

            {totalPages > 1 ? (
              <div className="seller-toolbar">
                <span className="seller-lead">
                  {from}–{to} of {totalCount}
                </span>
                <div className="seller-form-actions">
                  <button
                    type="button"
                    className="btn btn-secondary"
                    disabled={page <= 1 || loading}
                    onClick={() => setPage((p) => Math.max(1, p - 1))}
                  >
                    Previous
                  </button>
                  <button
                    type="button"
                    className="btn btn-secondary"
                    disabled={page >= totalPages || loading}
                    onClick={() => setPage((p) => p + 1)}
                  >
                    Next
                  </button>
                </div>
              </div>
            ) : null}
          </>
        )}
      </div>
    </div>
  )
}
