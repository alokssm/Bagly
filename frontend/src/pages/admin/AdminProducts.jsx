import { useCallback, useEffect, useState } from 'react'
import { api } from '../../api/client'
import { formatPrice } from '../../utils/format'

const PAGE_SIZE = 50
const emptyResult = { items: [], totalCount: 0, totalPages: 0, page: 1, pageSize: PAGE_SIZE }

function shippingDraftFromProduct(p) {
  return {
    shiprocketPickupLocation: p.shiprocketPickupLocation || '',
    useDefaultPackageSize: p.useDefaultPackageSize !== false,
    weightKg: p.weightKg != null ? String(p.weightKg) : '',
    lengthCm: p.lengthCm != null ? String(p.lengthCm) : '',
    breadthCm: p.breadthCm != null ? String(p.breadthCm) : '',
    heightCm: p.heightCm != null ? String(p.heightCm) : '',
  }
}

function shippingSnapshot(draft) {
  return {
    shiprocketPickupLocation: String(draft.shiprocketPickupLocation || '').trim() || null,
    useDefaultPackageSize: draft.useDefaultPackageSize !== false,
    weightKg:
      draft.useDefaultPackageSize === false && draft.weightKg !== ''
        ? Number(draft.weightKg)
        : null,
    lengthCm:
      draft.useDefaultPackageSize === false && draft.lengthCm !== ''
        ? Number(draft.lengthCm)
        : null,
    breadthCm:
      draft.useDefaultPackageSize === false && draft.breadthCm !== ''
        ? Number(draft.breadthCm)
        : null,
    heightCm:
      draft.useDefaultPackageSize === false && draft.heightCm !== ''
        ? Number(draft.heightCm)
        : null,
  }
}

function isShippingDirty(draft, product) {
  const a = shippingSnapshot(draft)
  const b = shippingSnapshot(shippingDraftFromProduct(product))
  return JSON.stringify(a) !== JSON.stringify(b)
}

export default function AdminProducts() {
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [result, setResult] = useState(emptyResult)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [pickupChoices, setPickupChoices] = useState(['home', 'work'])
  const [shippingDrafts, setShippingDrafts] = useState({})
  const [savingId, setSavingId] = useState('')

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
        drafts[p.id] = shippingDraftFromProduct(p)
      }
      setShippingDrafts(drafts)
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

  const updateDraft = (productId, patch) => {
    setShippingDrafts((prev) => ({
      ...prev,
      [productId]: { ...prev[productId], ...patch },
    }))
  }

  const saveShipping = async (productId) => {
    setSavingId(productId)
    setError('')
    try {
      const draft = shippingDrafts[productId] || shippingDraftFromProduct({})
      if (draft.useDefaultPackageSize === false) {
        const missing =
          !draft.weightKg ||
          !draft.lengthCm ||
          !draft.breadthCm ||
          !draft.heightCm ||
          Number(draft.weightKg) <= 0 ||
          Number(draft.lengthCm) <= 0 ||
          Number(draft.breadthCm) <= 0 ||
          Number(draft.heightCm) <= 0
        if (missing) {
          setError('Custom package requires weight (kg) and length/breadth/height (cm) greater than 0.')
          return
        }
      }

      const body = shippingSnapshot(draft)
      const updated = await api.adminPatchProductShipping(productId, body)
      setResult((prev) => ({
        ...prev,
        items: prev.items.map((p) =>
          p.id === productId
            ? {
                ...p,
                shiprocketPickupLocation: updated.shiprocketPickupLocation || null,
                useDefaultPackageSize: updated.useDefaultPackageSize !== false,
                weightKg: updated.weightKg ?? null,
                lengthCm: updated.lengthCm ?? null,
                breadthCm: updated.breadthCm ?? null,
                heightCm: updated.heightCm ?? null,
              }
            : p,
        ),
      }))
      setShippingDrafts((prev) => ({
        ...prev,
        [productId]: shippingDraftFromProduct(updated),
      }))
    } catch (err) {
      const status = err?.status
      if (status === 404) {
        setError(
          'Shipping save API not found (404). Confirm Render is on the latest main deploy, then retry.',
        )
      } else if (status === 401) {
        setError('Admin session expired. Sign in again, then save shipping.')
      } else {
        setError(err.message || 'Unable to update shipping package.')
      }
    } finally {
      setSavingId('')
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
            Sellers manage listings. Admins can set Shiprocket pickup and package size for platform or seller
            products.
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
                <th>Shiprocket shipping</th>
              </tr>
            </thead>
            <tbody>
              {products.map((product) => {
                const draft = shippingDrafts[product.id] || shippingDraftFromProduct(product)
                const dirty = isShippingDirty(draft, product)
                const pickupDraft = draft.shiprocketPickupLocation || ''
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
                      <div
                        style={{
                          display: 'flex',
                          flexDirection: 'column',
                          gap: '0.4rem',
                          minWidth: '14rem',
                        }}
                      >
                        <div style={{ display: 'flex', gap: '0.35rem', alignItems: 'center', flexWrap: 'wrap' }}>
                          <select
                            value={
                              pickupChoices.includes(pickupDraft) || pickupDraft === ''
                                ? pickupDraft
                                : '__custom__'
                            }
                            onChange={(e) => {
                              const v = e.target.value
                              if (v === '__custom__') {
                                updateDraft(product.id, {
                                  shiprocketPickupLocation: pickupDraft || '',
                                })
                                return
                              }
                              updateDraft(product.id, { shiprocketPickupLocation: v })
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
                          {!pickupChoices.includes(pickupDraft) && pickupDraft !== '' ? (
                            <input
                              style={{ width: '6.5rem' }}
                              value={pickupDraft}
                              onChange={(e) =>
                                updateDraft(product.id, { shiprocketPickupLocation: e.target.value })
                              }
                              placeholder="nickname"
                            />
                          ) : null}
                        </div>

                        <label className="admin-check" style={{ fontSize: '0.85rem' }}>
                          <input
                            type="checkbox"
                            checked={draft.useDefaultPackageSize !== false}
                            onChange={(e) =>
                              updateDraft(product.id, { useDefaultPackageSize: e.target.checked })
                            }
                          />
                          Default package size
                        </label>

                        {draft.useDefaultPackageSize === false ? (
                          <div
                            style={{
                              display: 'grid',
                              gridTemplateColumns: 'repeat(2, minmax(4.5rem, 1fr))',
                              gap: '0.3rem',
                            }}
                          >
                            <input
                              type="number"
                              min="0.01"
                              step="0.01"
                              placeholder="kg"
                              value={draft.weightKg}
                              onChange={(e) => updateDraft(product.id, { weightKg: e.target.value })}
                              aria-label={`Weight for ${product.name}`}
                            />
                            <input
                              type="number"
                              min="0.01"
                              step="0.01"
                              placeholder="L cm"
                              value={draft.lengthCm}
                              onChange={(e) => updateDraft(product.id, { lengthCm: e.target.value })}
                              aria-label={`Length for ${product.name}`}
                            />
                            <input
                              type="number"
                              min="0.01"
                              step="0.01"
                              placeholder="B cm"
                              value={draft.breadthCm}
                              onChange={(e) => updateDraft(product.id, { breadthCm: e.target.value })}
                              aria-label={`Breadth for ${product.name}`}
                            />
                            <input
                              type="number"
                              min="0.01"
                              step="0.01"
                              placeholder="H cm"
                              value={draft.heightCm}
                              onChange={(e) => updateDraft(product.id, { heightCm: e.target.value })}
                              aria-label={`Height for ${product.name}`}
                            />
                          </div>
                        ) : null}

                        <button
                          type="button"
                          className="btn btn-secondary btn-sm"
                          disabled={!dirty || savingId === product.id}
                          onClick={() => saveShipping(product.id)}
                        >
                          {savingId === product.id ? 'Saving…' : 'Save shipping'}
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
