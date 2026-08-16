import { useCallback, useEffect, useMemo, useState } from 'react'
import { api } from '../api/client'
import SellerHubNav from '../components/SellerHubNav'
import { useSellerAuth } from '../context/SellerAuthContext'
import { formatPrice } from '../utils/format'
import {
  exportRowsToExcel,
  exportRowsToPdf,
  sellerOrdersToExportRows,
} from '../utils/orderExport'

const money = (value) => formatPrice(value, { fractionDigits: 2 })

function toInputDate(d) {
  const pad = (n) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

function defaultDateRange() {
  const to = new Date()
  const from = new Date()
  from.setDate(from.getDate() - 29)
  return { from: toInputDate(from), to: toInputDate(to) }
}

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
  const defaults = useMemo(() => defaultDateRange(), [])
  const [filters, setFilters] = useState({ ...defaults, status: '' })
  const [applied, setApplied] = useState({ ...defaults, status: '' })
  const [orders, setOrders] = useState([])
  const [totalCount, setTotalCount] = useState(0)
  const [loading, setLoading] = useState(approved)
  const [exporting, setExporting] = useState('')
  const [error, setError] = useState('')

  const load = useCallback(async () => {
    if (!approved) {
      setLoading(false)
      return
    }
    if (!applied.from || !applied.to) {
      setError('From and To dates are required.')
      setOrders([])
      setTotalCount(0)
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
      })
      setOrders(data.items || [])
      setTotalCount(data.totalCount || 0)
    } catch (err) {
      setError(err.message || 'Unable to load report.')
      setOrders([])
      setTotalCount(0)
    } finally {
      setLoading(false)
    }
  }, [approved, applied])

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
    setApplied({ ...filters })
  }

  const runExport = async (kind) => {
    if (!orders.length) return
    setExporting(kind)
    setError('')
    try {
      const { headers, rows } = sellerOrdersToExportRows(orders)
      const subtitle = `From ${applied.from} to ${applied.to}${
        applied.status ? ` · Status: ${applied.status}` : ''
      } · ${orders.length} order${orders.length === 1 ? '' : 's'}`
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
                  disabled={!orders.length || !!exporting || loading}
                  onClick={() => runExport('excel')}
                >
                  {exporting === 'excel' ? 'Exporting…' : 'Export Excel'}
                </button>
                <button
                  type="button"
                  className="btn btn-secondary"
                  disabled={!orders.length || !!exporting || loading}
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

            <p className="seller-lead">
              {loading
                ? 'Loading report…'
                : totalCount === 0
                  ? 'No orders in this range.'
                  : `${orders.length} of ${totalCount} order${totalCount === 1 ? '' : 's'} shown`}
            </p>

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
          </>
        )}
      </div>
    </div>
  )
}
