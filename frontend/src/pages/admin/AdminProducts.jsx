import { useCallback, useEffect, useState } from 'react'
import { api } from '../../api/client'
import { formatPrice } from '../../utils/format'

const PAGE_SIZE = 50
const emptyResult = { items: [], totalCount: 0, totalPages: 0, page: 1, pageSize: PAGE_SIZE }

export default function AdminProducts() {
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [result, setResult] = useState(emptyResult)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [pickupChoices, setPickupChoices] = useState(['home', 'work'])
  const [pickupDrafts, setPickupDrafts] = useState({})
  const [savingPickupId, setSavingPickupId] = useState('')

  useEffect(() => {
    const handle = setTimeout(() => {
      setSearch(searchInput.trim())
      setPage(1)
    }, 300)
    return () => clearTimeout(handle)
  }, [searchInput])

  useEffect(() => {
    let cancelled = false
    api
      .adminGetShiprocketPickupLocations()
      .then((data) => {
        if (cancelled) return
        const locations = (data?.locations || []).filter(Boolean)
        if (locations.length) setPickupChoices(locations)
      })
      .catch(() => {})
    return () => {
      cancelled = true
    }
  }, [])

  const load = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      const data = await api.adminGetProducts({ page, pageSize: PAGE_SIZE, search: search || undefined })
      setResult({
        items: data.items || [],
        totalCount: data.totalCount || 0,
        totalPages: data.totalPages || 0,
        page: data.page || page,
        pageSize: data.pageSize || PAGE_SIZE,
      })
      const drafts = {}
      for (const p of data.items || []) {
        drafts[p.id] = p.shiprocketPickupLocation || ''
      }
      setPickupDrafts(drafts)
    } catch (err) {
      setError(err.message || 'Unable to load products.')
      setResult(emptyResult)
    } finally {
      setLoading(false)
    }
  }, [page, search])

  useEffect(() => {
    load()
  }, [load])

  const savePickup = async (productId) => {
    setSavingPickupId(productId)
    setError('')
    try {
      const value = String(pickupDrafts[productId] || '').trim() || null
      const updated = await api.adminPatchProductPickupLocation(productId, value)
      setResult((prev) => ({
        ...prev,
        items: prev.items.map((p) =>
          p.id === productId
            ? { ...p, shiprocketPickupLocation: updated.shiprocketPickupLocation || null }
            : p,
        ),
      }))
      setPickupDrafts((prev) => ({
        ...prev,
        [productId]: updated.shiprocketPickupLocation || '',
      }))
    } catch (err) {
      const status = err?.status
      if (status === 404) {
        setError(
          'Pickup save API not found (404). Confirm Render is on the latest main deploy (/api/health → shiprocket.multiPickupApi=true), then retry.',
        )
      } else if (status === 401) {
        setError('Admin session expired. Sign in again, then save pickup.')
      } else {
        setError(err.message || 'Unable to update pickup location.')
      }
    } finally {
      setSavingPickupId('')
    }
  }

  const { items: products, totalCount, totalPages } = result
  const from = totalCount === 0 ? 0 : (result.page - 1) * result.pageSize + 1
  const to = Math.min(result.page * result.pageSize, totalCount)

  return (
    <div className="admin-page">
      <div className="admin-page-head">
        <div>
          <p className="eyebrow">Catalog</p>
          <h1>Products</h1>
          <p className="admin-muted" style={{ marginTop: '0.35rem' }}>
            Sellers manage listings. Admins can assign Shiprocket pickup nicknames (home / work) for platform
            or seller products.
          </p>
        </div>
      </div>

      <div className="admin-search-bar">
        <label>
          Search
          <input
            type="search"
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
            placeholder="Name, ID, or category…"
          />
        </label>
      </div>

      {error ? <p className="admin-error">{error}</p> : null}

      <div className="admin-table-wrap">
        {loading ? (
          <p className="admin-muted">Loading products…</p>
        ) : products.length === 0 ? (
          <p className="admin-muted">{search ? `No products match "${search}".` : 'No products yet.'}</p>
        ) : (
          <table className="admin-table">
            <thead>
              <tr>
                <th>Product</th>
                <th>Category</th>
                <th>Owner</th>
                <th>Price</th>
                <th>Stock</th>
                <th>Status</th>
                <th>Shiprocket pickup</th>
              </tr>
            </thead>
            <tbody>
              {products.map((product) => {
                const draft = pickupDrafts[product.id] ?? ''
                const saved = product.shiprocketPickupLocation || ''
                const dirty = draft !== saved
                return (
                  <tr key={product.id}>
                    <td>
                      <div className="admin-product-cell">
                        <img src={product.image} alt="" />
                        <div>
                          <strong>{product.name}</strong>
                          <small>{product.id}</small>
                        </div>
                      </div>
                    </td>
                    <td>
                      {product.category}
                      {product.subCategoryId ? <small> / {product.subCategoryId}</small> : null}
                    </td>
                    <td>
                      <small>{product.sellerId ? 'Seller' : 'Platform'}</small>
                    </td>
                    <td>{formatPrice(product.price)}</td>
                    <td>
                      <span className={`admin-pill ${product.stockQuantity > 0 ? 'on' : 'off'}`}>
                        {product.stockQuantity > 0 ? `${product.stockQuantity} in stock` : 'Out of stock'}
                      </span>
                    </td>
                    <td>
                      <span className={`admin-pill ${product.isActive ? 'on' : 'off'}`}>
                        {product.isActive ? 'Active' : 'Hidden'}
                      </span>
                    </td>
                    <td>
                      <div style={{ display: 'flex', gap: '0.35rem', alignItems: 'center', flexWrap: 'wrap' }}>
                        <select
                          value={pickupChoices.includes(draft) || draft === '' ? draft : '__custom__'}
                          onChange={(e) => {
                            const v = e.target.value
                            if (v === '__custom__') {
                              setPickupDrafts((prev) => ({ ...prev, [product.id]: draft || '' }))
                              return
                            }
                            setPickupDrafts((prev) => ({ ...prev, [product.id]: v }))
                          }}
                          aria-label={`Pickup for ${product.name}`}
                        >
                          <option value="">Default</option>
                          {pickupChoices.map((nick) => (
                            <option key={nick} value={nick}>
                              {nick}
                            </option>
                          ))}
                          <option value="__custom__">Custom…</option>
                        </select>
                        {!pickupChoices.includes(draft) && draft !== '' ? (
                          <input
                            style={{ width: '6.5rem' }}
                            value={draft}
                            onChange={(e) =>
                              setPickupDrafts((prev) => ({ ...prev, [product.id]: e.target.value }))
                            }
                            placeholder="nickname"
                          />
                        ) : null}
                        <button
                          type="button"
                          className="btn btn-secondary btn-sm"
                          disabled={!dirty || savingPickupId === product.id}
                          onClick={() => savePickup(product.id)}
                        >
                          {savingPickupId === product.id ? 'Saving…' : 'Save'}
                        </button>
                      </div>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        )}
      </div>

      <div className="admin-pagination">
        <p className="admin-muted">
          {totalCount === 0 ? '0 products' : `Showing ${from}–${to} of ${totalCount}`}
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
            Page {totalPages === 0 ? 0 : page} of {totalPages}
          </span>
          <button
            type="button"
            className="btn btn-secondary"
            disabled={page >= totalPages || loading || totalPages === 0}
            onClick={() => setPage((p) => p + 1)}
          >
            Next
          </button>
        </div>
      </div>
    </div>
  )
}
