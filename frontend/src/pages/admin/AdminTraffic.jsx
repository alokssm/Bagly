import { useCallback, useEffect, useState } from 'react'
import { api } from '../../api/client'

const emptyData = {
  totalHits: 0,
  uniqueSessions: 0,
  locations: [],
}

export default function AdminTraffic() {
  const [filters, setFilters] = useState({ from: '', to: '' })
  const [applied, setApplied] = useState({ from: '', to: '' })
  const [data, setData] = useState(emptyData)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const load = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      const result = await api.adminGetLocationAnalytics({
        from: applied.from || undefined,
        to: applied.to || undefined,
      })
      setData({ ...emptyData, ...result })
    } catch (err) {
      setError(err.message || 'Unable to load traffic analytics.')
      setData(emptyData)
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

  const rangeActive = Boolean(applied.from || applied.to)
  const maxHits = Math.max(1, ...data.locations.map((l) => l.hits))

  return (
    <div className="admin-page">
      <div className="admin-page-head">
        <div>
          <p className="eyebrow">Visitors</p>
          <h1>Traffic</h1>
          <p className="admin-subtitle">
            Page views by visitor location, resolved from IP address (ip-api.com, cached 24h per
            IP to respect its free 45 requests/min limit). Private/local IPs are grouped as
            &ldquo;Local&rdquo;; lookups that fail or time out show as &ldquo;Unknown&rdquo;.
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
        <p className="admin-muted">Loading traffic…</p>
      ) : (
        <>
          <div className="admin-stats">
            <div className="admin-stat">
              <span>{rangeActive ? 'Hits in range' : 'Total hits'}</span>
              <strong>{data.totalHits}</strong>
            </div>
            <div className="admin-stat">
              <span>Unique sessions</span>
              <strong>{data.uniqueSessions}</strong>
            </div>
            <div className="admin-stat">
              <span>Countries reached</span>
              <strong>{data.locations.length}</strong>
            </div>
          </div>

          <div className="admin-panel">
            <h2>Hits by location</h2>
            {data.locations.length === 0 ? (
              <p className="admin-muted">No storefront visits recorded for the current filters.</p>
            ) : (
              <>
                <div className="analytics-bars">
                  {data.locations.slice(0, 10).map((row) => (
                    <div className="analytics-bar-row" key={row.country}>
                      <span className="analytics-bar-label">{row.country}</span>
                      <div className="analytics-bar-track">
                        <div
                          className="analytics-bar-fill"
                          style={{ width: `${(row.hits / maxHits) * 100}%` }}
                        />
                      </div>
                      <span className="analytics-bar-value">{row.hits}</span>
                    </div>
                  ))}
                </div>

                <div className="admin-table-wrap">
                  <table className="admin-table">
                    <thead>
                      <tr>
                        <th>Country</th>
                        <th>Hits</th>
                        <th>Unique sessions</th>
                        <th>%</th>
                      </tr>
                    </thead>
                    <tbody>
                      {data.locations.map((row) => (
                        <tr key={row.country}>
                          <td>{row.country}</td>
                          <td>{row.hits}</td>
                          <td>{row.uniqueSessions}</td>
                          <td>{row.percentage.toFixed(1)}%</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
                <p className="admin-muted">Showing top {data.locations.length} location(s) (max 50).</p>
              </>
            )}
          </div>
        </>
      )}
    </div>
  )
}
