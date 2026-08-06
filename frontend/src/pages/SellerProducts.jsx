import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import { useSellerAuth } from '../context/SellerAuthContext'
import { formatPrice } from '../utils/format'
import SellerHubNav from '../components/SellerHubNav'

const PAGE_SIZE = 50
const emptyResult = { items: [], totalCount: 0, totalPages: 0, page: 1, pageSize: PAGE_SIZE }

export default function SellerProducts() {
  const { user, logout } = useSellerAuth()
  const approved = (user?.status || '').toLowerCase() === 'approved'
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [result, setResult] = useState(emptyResult)
  const [loading, setLoading] = useState(approved)
  const [error, setError] = useState('')
  const [busyId, setBusyId] = useState('')

  useEffect(() => {
    const handle = setTimeout(() => {
      setSearch(searchInput.trim())
      setPage(1)
    }, 300)
    return () => clearTimeout(handle)
  }, [searchInput])

  const load = useCallback(async () => {
    if (!approved) {
      setLoading(false)
      return
    }
    setLoading(true)
    setError('')
    try {
      const data = await api.sellerGetProducts({ page, pageSize: PAGE_SIZE, search: search || undefined })
      setResult({
        items: data.items || [],
        totalCount: data.totalCount || 0,
        totalPages: data.totalPages || 0,
        page: data.page || page,
        pageSize: data.pageSize || PAGE_SIZE,
      })
    } catch (err) {
      setError(err.message || 'Unable to load products.')
      setResult(emptyResult)
    } finally {
      setLoading(false)
    }
  }, [approved, page, search])

  useEffect(() => {
    load()
  }, [load])

  const handleDelete = async (id, name) => {
    if (!window.confirm(`Delete product "${name}"? This cannot be undone.`)) return
    setBusyId(id)
    try {
      await api.sellerDeleteProduct(id)
      if (result.items.length === 1 && page > 1) {
        setPage((p) => p - 1)
      } else {
        await load()
      }
    } catch (err) {
      setError(err.message || 'Delete failed.')
    } finally {
      setBusyId('')
    }
  }

  const { items: products, totalCount, totalPages } = result
  const from = totalCount === 0 ? 0 : (result.page - 1) * result.pageSize + 1
  const to = Math.min(result.page * result.pageSize, totalCount)

  return (
    <div className="seller-page">
      <div className="seller-shell seller-shell--wide">
        <header className="seller-head">
          <div>
            <p className="eyebrow">Seller hub</p>
            <h1>Products</h1>
            <p className="seller-lead">List and manage bags you sell on Bagly.</p>
          </div>
          <button type="button" className="btn btn-ghost" onClick={logout}>
            Sign out
          </button>
        </header>

        <SellerHubNav />

        {!approved ? (
          <div className="seller-status seller-status--pending" role="status">
            <strong>Awaiting admin approval</strong>
            <span>Product management is locked until your seller account is approved.</span>
          </div>
        ) : (
          <>
            <div className="seller-toolbar">
              <label className="seller-search">
                Search
                <input
                  type="search"
                  value={searchInput}
                  onChange={(e) => setSearchInput(e.target.value)}
                  placeholder="Name or ID…"
                />
              </label>
              <Link to="/seller/products/new" className="btn btn-primary">
                Add product
              </Link>
            </div>

            {error ? <p className="admin-error">{error}</p> : null}

            <div className="admin-table-wrap seller-table-wrap">
              {loading ? (
                <p className="admin-muted">Loading products…</p>
              ) : products.length === 0 ? (
                <p className="admin-muted">
                  {search ? `No products match "${search}".` : 'No products yet. Add your first listing.'}
                </p>
              ) : (
                <table className="admin-table">
                  <thead>
                    <tr>
                      <th>Product</th>
                      <th>Category</th>
                      <th>Price</th>
                      <th>Stock</th>
                      <th>Status</th>
                      <th />
                    </tr>
                  </thead>
                  <tbody>
                    {products.map((product) => (
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
                        <td className="admin-row-actions">
                          <Link to={`/seller/products/${product.id}/edit`} className="btn btn-secondary">
                            Edit
                          </Link>
                          <button
                            type="button"
                            className="btn btn-danger"
                            disabled={busyId === product.id}
                            onClick={() => handleDelete(product.id, product.name)}
                          >
                            {busyId === product.id ? 'Deleting…' : 'Delete'}
                          </button>
                        </td>
                      </tr>
                    ))}
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
          </>
        )}
      </div>
    </div>
  )
}
