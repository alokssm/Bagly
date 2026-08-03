import { useCallback, useEffect, useState } from 'react'
import { api } from '../../api/client'
import { formatPrice } from '../../utils/format'

const emptyAnalytics = {
  totalOrders: 0,
  totalRevenue: 0,
  averageOrderValue: 0,
  ordersToday: 0,
  ordersThisWeek: 0,
  ordersThisMonth: 0,
  ordersByStatus: [],
  topProducts: [],
}

export default function AdminAnalytics() {
  const [filters, setFilters] = useState({ from: '', to: '' })
  const [applied, setApplied] = useState({ from: '', to: '' })
  const [data, setData] = useState(emptyAnalytics)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const load = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      const result = await api.adminGetAnalytics({
        from: applied.from || undefined,
        to: applied.to || undefined,
      })
      setData({ ...emptyAnalytics, ...result })
    } catch (err) {
      setError(err.message || 'Unable to load analytics.')
      setData(emptyAnalytics)
    } finally {
      setLoading(false)
    }
  }, [applied])

  useEffect(() => {
    load()
  }, [load])

  const applyFilters = (e) => {
    e.preventDefault()
    setApplied({ ...filters })
  }

  const clearFilters = () => {
    const empty = { from: '', to: '' }
    setFilters(empty)
    setApplied(empty)
  }

  const maxStatusCount = Math.max(1, ...data.ordersByStatus.map((s) => s.count))
  const maxProductQty = Math.max(1, ...data.topProducts.map((p) => p.quantitySold))
  const rangeActive = Boolean(applied.from || applied.to)

  return (
    <div className="admin-page">
      <div className="admin-page-head">
        <div>
          <p className="eyebrow">Insights</p>
          <h1>Analytics</h1>
          <p className="admin-subtitle">
            Revenue counts Confirmed orders only. Today/this week/this month always use India Standard
            Time and are independent of the date range filter below.
          </p>
        </div>
      </div>

      {error ? <p className="admin-error">{error}</p> : null}

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
        <div className="admin-filters-actions">
          <button type="submit" className="btn btn-primary">
            Apply
          </button>
          <button type="button" className="btn btn-secondary" onClick={clearFilters}>
            Clear
          </button>
        </div>
      </form>

      {loading ? (
        <p className="admin-muted">Loading analytics…</p>
      ) : (
        <>
          <div className="admin-stats">
            <div className="admin-stat">
              <span>{rangeActive ? 'Orders in range' : 'Total orders'}</span>
              <strong>{data.totalOrders}</strong>
            </div>
            <div className="admin-stat">
              <span>Revenue{rangeActive ? ' (range)' : ''}</span>
              <strong>{formatPrice(data.totalRevenue)}</strong>
            </div>
            <div className="admin-stat">
              <span>Avg. order value</span>
              <strong>{formatPrice(data.averageOrderValue)}</strong>
            </div>
          </div>

          <div className="admin-stats">
            <div className="admin-stat">
              <span>Orders today (IST)</span>
              <strong>{data.ordersToday}</strong>
            </div>
            <div className="admin-stat">
              <span>Orders this week (IST)</span>
              <strong>{data.ordersThisWeek}</strong>
            </div>
            <div className="admin-stat">
              <span>Orders this month (IST)</span>
              <strong>{data.ordersThisMonth}</strong>
            </div>
          </div>

          <div className="admin-split">
            <div className="admin-panel">
              <h2>Orders by status</h2>
              {data.ordersByStatus.length === 0 ? (
                <p className="admin-muted">No orders found for the current filters.</p>
              ) : (
                <div className="analytics-bars">
                  {data.ordersByStatus.map((row) => (
                    <div className="analytics-bar-row" key={row.status}>
                      <span className="analytics-bar-label">{row.status}</span>
                      <div className="analytics-bar-track">
                        <div
                          className="analytics-bar-fill"
                          style={{ width: `${(row.count / maxStatusCount) * 100}%` }}
                        />
                      </div>
                      <span className="analytics-bar-value">{row.count}</span>
                    </div>
                  ))}
                </div>
              )}
            </div>

            <div className="admin-panel">
              <h2>Top products by quantity sold</h2>
              {data.topProducts.length === 0 ? (
                <p className="admin-muted">No completed sales found for the current filters.</p>
              ) : (
                <div className="admin-table-wrap">
                  <table className="admin-table">
                    <thead>
                      <tr>
                        <th>Product</th>
                        <th>Qty sold</th>
                        <th>Revenue</th>
                      </tr>
                    </thead>
                    <tbody>
                      {data.topProducts.map((p) => (
                        <tr key={p.productId}>
                          <td>
                            {p.productName}
                            <div className="analytics-bar-track analytics-bar-track-sm">
                              <div
                                className="analytics-bar-fill"
                                style={{ width: `${(p.quantitySold / maxProductQty) * 100}%` }}
                              />
                            </div>
                          </td>
                          <td>{p.quantitySold}</td>
                          <td>{formatPrice(p.revenue)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>
          </div>
        </>
      )}
    </div>
  )
}
