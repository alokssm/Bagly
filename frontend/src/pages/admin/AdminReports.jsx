import { Fragment, useCallback, useEffect, useState } from 'react'
import { api } from '../../api/client'

const PAGE_SIZE = 50

function formatDate(value) {
  if (!value) return '—'
  try {
    return new Date(value).toLocaleString()
  } catch {
    return String(value)
  }
}

function levelClass(level) {
  const l = (level || '').toLowerCase()
  if (l === 'error' || l === 'fatal') return 'log-level error'
  if (l === 'warning') return 'log-level warning'
  if (l === 'information' || l === 'info') return 'log-level info'
  return 'log-level'
}

export default function AdminReports() {
  const [tab, setTab] = useState('audit')
  const [summary, setSummary] = useState(null)
  const [page, setPage] = useState(1)
  const [result, setResult] = useState({ items: [], totalCount: 0, totalPages: 0, page: 1, pageSize: PAGE_SIZE })
  const [filters, setFilters] = useState({
    category: '',
    level: '',
    action: '',
    search: '',
  })
  const [applied, setApplied] = useState({
    category: '',
    level: '',
    action: '',
    search: '',
  })
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [expandedId, setExpandedId] = useState(null)

  const loadSummary = useCallback(async () => {
    try {
      const data = await api.adminGetReportSummary()
      setSummary(data)
    } catch {
      // summary is optional; table errors still surface below
    }
  }, [])

  const loadLogs = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      const params = {
        page,
        pageSize: PAGE_SIZE,
        search: applied.search || undefined,
        level: applied.level || undefined,
      }

      const data =
        tab === 'audit'
          ? await api.adminGetAuditLogs({
              ...params,
              category: applied.category || undefined,
              action: applied.action || undefined,
            })
          : await api.adminGetSystemLogs(params)

      setResult({
        items: data.items || [],
        totalCount: data.totalCount || 0,
        totalPages: data.totalPages || 0,
        page: data.page || page,
        pageSize: data.pageSize || PAGE_SIZE,
      })
    } catch (err) {
      setError(err.message || 'Unable to load logs.')
      setResult({ items: [], totalCount: 0, totalPages: 0, page: 1, pageSize: PAGE_SIZE })
    } finally {
      setLoading(false)
    }
  }, [tab, page, applied])

  useEffect(() => {
    loadSummary()
  }, [loadSummary])

  useEffect(() => {
    loadLogs()
  }, [loadLogs])

  const applyFilters = (e) => {
    e.preventDefault()
    setPage(1)
    setExpandedId(null)
    setApplied({ ...filters })
  }

  const clearFilters = () => {
    const empty = { category: '', level: '', action: '', search: '' }
    setFilters(empty)
    setApplied(empty)
    setPage(1)
    setExpandedId(null)
  }

  const switchTab = (next) => {
    setTab(next)
    setPage(1)
    setExpandedId(null)
  }

  const from = result.totalCount === 0 ? 0 : (result.page - 1) * result.pageSize + 1
  const to = Math.min(result.page * result.pageSize, result.totalCount)

  return (
    <div className="admin-page admin-reports">
      <div className="admin-page-head">
        <div>
          <p className="eyebrow">Monitoring</p>
          <h1>Reports &amp; logs</h1>
          <p className="admin-subtitle">Admin-only view of audit and system logs (50 per page).</p>
        </div>
        <button
          type="button"
          className="btn btn-secondary btn-sm"
          onClick={() => {
            loadSummary()
            loadLogs()
          }}
          disabled={loading}
        >
          {loading ? 'Refreshing…' : 'Refresh'}
        </button>
      </div>

      {summary ? (
        <div className="admin-stats admin-stats-4">
          <div className="admin-stat">
            <span>Audit logs</span>
            <strong>{summary.auditLogCount}</strong>
          </div>
          <div className="admin-stat">
            <span>System logs</span>
            <strong>{summary.systemLogCount}</strong>
          </div>
          <div className="admin-stat">
            <span>Errors</span>
            <strong>{summary.errorCount}</strong>
          </div>
          <div className="admin-stat">
            <span>Warnings</span>
            <strong>{summary.warningCount}</strong>
          </div>
        </div>
      ) : null}

      <div className="admin-tabs" role="tablist">
        <button
          type="button"
          role="tab"
          aria-selected={tab === 'audit'}
          className={tab === 'audit' ? 'admin-tab active' : 'admin-tab'}
          onClick={() => switchTab('audit')}
        >
          Audit logs
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={tab === 'system'}
          className={tab === 'system' ? 'admin-tab active' : 'admin-tab'}
          onClick={() => switchTab('system')}
        >
          System logs
        </button>
      </div>

      <form
        className={`admin-filters admin-reports-filters${tab === 'system' ? ' admin-reports-filters--system' : ''}`}
        onSubmit={applyFilters}
      >
        <label>
          Search
          <input
            type="search"
            value={filters.search}
            onChange={(e) => setFilters((f) => ({ ...f, search: e.target.value }))}
            placeholder="Message, email, path…"
          />
        </label>
        <label>
          Level
          <select
            value={filters.level}
            onChange={(e) => setFilters((f) => ({ ...f, level: e.target.value }))}
          >
            <option value="">All</option>
            <option value="Information">Information</option>
            <option value="Warning">Warning</option>
            <option value="Error">Error</option>
            <option value="Fatal">Fatal</option>
            <option value="Debug">Debug</option>
          </select>
        </label>
        {tab === 'audit' ? (
          <>
            <label>
              Category
              <select
                value={filters.category}
                onChange={(e) => setFilters((f) => ({ ...f, category: e.target.value }))}
              >
                <option value="">All</option>
                <option value="Auth">Auth</option>
                <option value="Product">Product</option>
                <option value="Category">Category</option>
                <option value="Order">Order</option>
                <option value="System">System</option>
                <option value="General">General</option>
              </select>
            </label>
            <label>
              Action
              <input
                type="text"
                value={filters.action}
                onChange={(e) => setFilters((f) => ({ ...f, action: e.target.value }))}
                placeholder="Login, Create…"
              />
            </label>
          </>
        ) : null}
        <div className="admin-filters-actions">
          <button type="submit" className="btn btn-primary btn-sm">
            Apply
          </button>
          <button type="button" className="btn btn-secondary btn-sm" onClick={clearFilters}>
            Clear
          </button>
        </div>
      </form>

      {error ? <p className="admin-error">{error}</p> : null}

      <div className="admin-table-wrap">
        {loading ? (
          <p className="admin-muted admin-reports-empty">Loading logs…</p>
        ) : result.items.length === 0 ? (
          <p className="admin-muted admin-reports-empty">No logs found for the current filters.</p>
        ) : tab === 'audit' ? (
          <table className="admin-table admin-logs-table">
            <thead>
              <tr>
                <th>Time (UTC)</th>
                <th>Level</th>
                <th>Category</th>
                <th>Action</th>
                <th>Actor</th>
                <th>Message</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {result.items.map((row) => (
                <Fragment key={row.id}>
                  <tr>
                    <td className="nowrap">{formatDate(row.timestampUtc)}</td>
                    <td>
                      <span className={levelClass(row.level)}>{row.level}</span>
                    </td>
                    <td>{row.category}</td>
                    <td>{row.action}</td>
                    <td>{row.actorEmail || '—'}</td>
                    <td className="log-message">{row.message}</td>
                    <td>
                      <button
                        type="button"
                        className="btn btn-secondary btn-sm"
                        onClick={() => setExpandedId(expandedId === row.id ? null : row.id)}
                      >
                        {expandedId === row.id ? 'Hide' : 'Details'}
                      </button>
                    </td>
                  </tr>
                  {expandedId === row.id ? (
                    <tr className="log-detail-row">
                      <td colSpan={7}>
                        <div className="log-detail">
                          <p>
                            <strong>Entity:</strong> {row.entityType || '—'} {row.entityId || ''}
                          </p>
                          <p>
                            <strong>Path:</strong> {row.requestPath || '—'}
                          </p>
                          <p>
                            <strong>IP:</strong> {row.ipAddress || '—'}
                          </p>
                          {row.detailsJson ? (
                            <pre>{row.detailsJson}</pre>
                          ) : (
                            <p className="admin-muted">No extra details.</p>
                          )}
                        </div>
                      </td>
                    </tr>
                  ) : null}
                </Fragment>
              ))}
            </tbody>
          </table>
        ) : (
          <table className="admin-table admin-logs-table">
            <thead>
              <tr>
                <th>Time</th>
                <th>Level</th>
                <th>Category</th>
                <th>Action</th>
                <th>Actor</th>
                <th>Message</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {result.items.map((row) => (
                <Fragment key={row.id}>
                  <tr>
                    <td className="nowrap">{formatDate(row.timeStamp)}</td>
                    <td>
                      <span className={levelClass(row.level)}>{row.level || '—'}</span>
                    </td>
                    <td>{row.auditCategory || '—'}</td>
                    <td>{row.auditAction || '—'}</td>
                    <td>{row.actorEmail || '—'}</td>
                    <td className="log-message">{row.message || '—'}</td>
                    <td>
                      <button
                        type="button"
                        className="btn btn-secondary btn-sm"
                        onClick={() => setExpandedId(expandedId === row.id ? null : row.id)}
                      >
                        {expandedId === row.id ? 'Hide' : 'Details'}
                      </button>
                    </td>
                  </tr>
                  {expandedId === row.id ? (
                    <tr className="log-detail-row">
                      <td colSpan={7}>
                        <div className="log-detail">
                          <p>
                            <strong>Path:</strong> {row.requestPath || '—'}
                          </p>
                          {row.exception ? (
                            <pre>{row.exception}</pre>
                          ) : (
                            <p className="admin-muted">No exception details.</p>
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
          {result.totalCount === 0
            ? '0 records'
            : `Showing ${from}–${to} of ${result.totalCount}`}
        </p>
        <div className="admin-pagination-controls">
          <button
            type="button"
            className="btn btn-secondary btn-sm"
            disabled={page <= 1 || loading}
            onClick={() => {
              setExpandedId(null)
              setPage((p) => Math.max(1, p - 1))
            }}
          >
            Previous
          </button>
          <span>
            Page {result.totalPages === 0 ? 0 : page} of {result.totalPages}
          </span>
          <button
            type="button"
            className="btn btn-secondary btn-sm"
            disabled={page >= result.totalPages || loading || result.totalPages === 0}
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
