import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../../api/client'
import { formatPrice } from '../../utils/format'

export default function AdminProducts() {
  const [products, setProducts] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [busyId, setBusyId] = useState('')

  const load = async () => {
    setLoading(true)
    setError('')
    try {
      const data = await api.adminGetProducts()
      setProducts(data)
    } catch (err) {
      setError(err.message || 'Unable to load products.')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    load()
  }, [])

  const handleDelete = async (id, name) => {
    if (!window.confirm(`Delete product "${name}"? This cannot be undone.`)) return
    setBusyId(id)
    try {
      await api.adminDeleteProduct(id)
      setProducts((prev) => prev.filter((p) => p.id !== id))
    } catch (err) {
      setError(err.message || 'Delete failed.')
    } finally {
      setBusyId('')
    }
  }

  return (
    <div className="admin-page">
      <div className="admin-page-head">
        <div>
          <p className="eyebrow">Catalog</p>
          <h1>Products</h1>
        </div>
        <Link to="/admin/products/new" className="btn btn-primary">
          Add product
        </Link>
      </div>

      {error ? <p className="admin-error">{error}</p> : null}
      {loading ? <p>Loading products…</p> : null}

      {!loading && products.length === 0 ? <p>No products yet.</p> : null}

      {products.length > 0 ? (
        <div className="admin-table-wrap">
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
                    <Link to={`/admin/products/${product.id}/edit`} className="btn btn-secondary">
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
        </div>
      ) : null}
    </div>
  )
}
