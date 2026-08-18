import { useCallback, useEffect, useState } from 'react'
import { api } from '../api/client'
import SellerHubNav from '../components/SellerHubNav'
import { useSellerAuth } from '../context/SellerAuthContext'
import { formatPrice } from '../utils/format'
import {
  exportRowsToExcel,
  exportRowsToPdf,
  sellerOrdersToExportRows,
} from '../utils/orderExport'

const PAGE_SIZE = 50
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

function itemsSummary(order) {
  const items = order.items || []
  if (!items.length) return '—'
  return items
    .map((i) => {
      const color = i.color ? ` · ${i.color}` : ''
      return `${i.productName}${color} ×${i.quantity}`
    })
    .join(', ')
}

export default function SellerReports() {
  const { user, logout } = useSellerAuth()
  const approved = (user?.status || '').toLowerCase() === 'approved'
  const [filters, setFilters] = useState({ from: '', to: '', status: '' })
  const [applied, setApplied] = useState(null)
  const [page, setPage] = useState(1)
  const [orders, setOrders] = useState([])
  const [totalCount, setTotalCount] = useState(0)
  const [totalPages, setTotalPages] = useState(0)
  const [loading, setLoading] = useState(false)
  const [exporting, setExporting] = useState('')
  const [error, setError] = useState('')

  const hasApplied = Boolean(applied?.from && applied?.to)

  const load = useCallback(async () => {
    if (!approved || !hasApplied) {
      setLoading(false)
      return
    }
    setLoading(true)
    setError('')
    try {
      const data = await api.sellerGetOrdersReport({
        from: applied.from,
        to: applied.to,
        status: applied.status || undefined,
        page,
        pageSize: PAGE_SIZE,
      })
      setOrders(data.items || [])
      setTotalCount(data.totalCount || 0)
      setTotalPages(data.totalPages || 0)
    } catch (err) {
      setError(err.message || 'Unable to load report.')
      setOrders([])
      setTotalCount(0)
      setTotalPages(0)
    } finally {
      setLoading(false)
    }
  }, [approved, applied, hasApplied, page])

  useEffect(() => {
    load()
  }, [load])

  const applyFilters = (e) => {
    e.preventDefault()
    if (!filters.from || !filters.to) {
      setError('From and To dates are required.')
      return
    }
    if (filters.from > filters.to) {
      setError('From date must be on or before To date.')
      return
    }
    setError('')
    setPage(1)
    setApplied({ ...filters })
  }

  const loadExportOrders = async () => {
    const base = {
      from: applied.from,
      to: applied.to,
      status: applied.status || undefined,
    }

    // Dedicated export endpoint (all matching rows, capped server-side).
    try {
      const data = await api.sellerExportOrdersReport(base)
      const items = data?.items || data?.Items || []
      if (items.length) return items
      // Empty success — nothing to export for this filter.
      if (data && (Array.isArray(data.items) || Array.isArray(data.Items))) return []
    } catch (err) {
      // Older API builds may lack /export (404). Fall back to paging /report.
      if (err?.status && err.status !== 404) throw err
    }

    // Fallback: page through the report list until all rows are collected.
    const all = []
    let pageNum = 1
    let totalPages = 1
    do {
      const data = await api.sellerGetOrdersReport({
        ...base,
        page: pageNum,
        pageSize: PAGE_SIZE,
      })
      const items = data?.items || data?.Items || []
      all.push(...items)
      const reportedPages = Number(data?.totalPages)
      if (Number.isFinite(reportedPages) && reportedPages > 0) {
        totalPages = reportedPages
      } else {
        // Legacy /report returned the full set in one response (no pagination).
        break
      }
      pageNum += 1
    } while (pageNum <= totalPages && pageNum <= 100)

    return all
  }

  const runExport = async (kind) => {
    if (!hasApplied) return
    setExporting(kind)
    setError('')
    try {
      const exportOrders = await loadExportOrders()
      if (!exportOrders.length) {
        setError('No orders to export for the current filters.')
        return
      }
      const { headers, rows } = sellerOrdersToExportRows(exportOrders)
      const subtitle = `From ${applied.from} to ${applied.to}${
        applied.status ? ` · Status: ${applied.status}` : ''
      } · ${exportOrders.length} order${exportOrders.length === 1 ? '' : 's'}`
      if (kind === 'excel') {
        exportRowsToExcel({
          filenameBase: 'bagly-seller-orders',
          headers,
          rows,
          sheetName: 'Seller Orders',
        })
      } else {
        exportRowsToPdf({
          filenameBase: 'bagly-seller-orders',
          title: 'Bagly Seller Order Report',
          subtitle,
          headers,
          rows,
        })
      }
    } catch (err) {
      setError(err.message || `Unable to export ${kind === 'excel' ? 'Excel' : 'PDF'}.`)
    } finally {
      setExporting('')
    }
  }

  const rangeFrom = totalCount === 0 ? 0 : (page - 1) * PAGE_SIZE + 1
  const rangeTo = Math.min(page * PAGE_SIZE, totalCount)

  let statusMessage = 'Choose a From and To date, then Apply to load orders.'
  if (loading) statusMessage = 'Loading report…'
  else if (hasApplied && totalCount === 0) statusMessage = 'No orders in this range.'
  else if (hasApplied)
    statusMessage = `Showing ${rangeFrom}–${rangeTo} of ${totalCount} order${totalCount === 1 ? '' : 's'}`

  return (
    <div className="seller-page">
      <div className="seller-shell seller-shell--wide">
        <header className="seller-head">
          <div>
            <p className="eyebrow">Seller hub</p>
            <h1>Reports</h1>
            <p className="seller-lead">
              Orders with your products for a date range. Export Excel or PDF using the same filters.
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
            <span>Reports are locked until your seller account is approved.</span>
          </div>
        ) : (
          <>
            <form className="seller-report-filters" onSubmit={applyFilters}>
              <label>
                From
                <input
                  type="date"
                  required
                  value={filters.from}
                  onChange={(e) => setFilters((f) => ({ ...f, from: e.target.value }))}
                />
              </label>
              <label>
                To
                <input
                  type="date"
                  required
                  value={filters.to}
                  onChange={(e) => setFilters((f) => ({ ...f, to: e.target.value }))}
                />
              </label>
              <label>
                Status
                <select
                  value={filters.status}
                  onChange={(e) => setFilters((f) => ({ ...f, status: e.target.value }))}
                >
                  <option value="">All</option>
                  <option value="Confirmed">Confirmed</option>
                  <option value="AwaitingPayment">AwaitingPayment</option>
                  <option value="Cancelled">Cancelled</option>
                </select>
              </label>
              <div className="seller-form-actions">
                <button type="submit" className="btn btn-primary" disabled={loading}>
                  Apply
                </button>
                <button
                  type="button"
                  className="btn btn-secondary"
                  disabled={!hasApplied || !!exporting || loading}
                  onClick={() => runExport('excel')}
                >
                  {exporting === 'excel' ? 'Exporting…' : 'Export Excel'}
                </button>
                <button
                  type="button"
                  className="btn btn-secondary"
                  disabled={!hasApplied || !!exporting || loading}
                  onClick={() => runExport('pdf')}
                >
                  {exporting === 'pdf' ? 'Exporting…' : 'Export PDF'}
                </button>
              </div>
            </form>

            {error ? (
              <p className="form-error" role="alert">
                {error}
              </p>
            ) : null}

            <p className="seller-lead">{statusMessage}</p>

            {!loading && orders.length > 0 ? (
              <div className="seller-table-wrap">
                <table className="seller-table">
                  <thead>
                    <tr>
                      <th>Order #</th>
                      <th>Date</th>
                      <th>Status</th>
                      <th>Customer</th>
                      <th>Items</th>
                      <th>Your subtotal</th>
                      <th>Payment</th>
                    </tr>
                  </thead>
                  <tbody>
                    {orders.map((order) => (
                      <tr key={order.id}>
                        <td className="nowrap">{order.orderNumber}</td>
                        <td className="nowrap">{formatWhen(order.createdAt)}</td>
                        <td>{order.status}</td>
                        <td>
                          {order.customerName}
                          {order.city ? (
                            <span className="seller-order-hint"> · {order.city}</span>
                          ) : null}
                        </td>
                        <td className="seller-report-items">{itemsSummary(order)}</td>
                        <td className="nowrap">{money(order.subtotal)}</td>
                        <td className="nowrap">
                          {order.paymentStatus}
                          {order.paymentProvider ? (
                            <span className="seller-order-hint"> / {order.paymentProvider}</span>
                          ) : null}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ) : null}

            {hasApplied && totalPages > 1 ? (
              <div className="admin-pagination">
                <p className="admin-muted">
                  Showing {rangeFrom}–{rangeTo} of {totalCount}
                </p>
                <div className="admin-pagination-controls">
                  <button
                    type="button"
                    className="btn btn-secondary"
                    disabled={page <= 1 || loading}
                    onClick={() => setPage((p) => Math.max(1, p - 1))}
                  >
                    Previous
                  </button>
                  <span>
                    Page {page} of {totalPages}
                  </span>
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
