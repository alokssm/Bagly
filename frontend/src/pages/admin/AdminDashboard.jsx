import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../../api/client'

export default function AdminDashboard() {
  const [stats, setStats] = useState({ products: 0, categories: 0, active: 0 })
  const [error, setError] = useState('')

  useEffect(() => {
    let cancelled = false

    async function load() {
      try {
        const [products, categories] = await Promise.all([
          api.adminGetProducts(),
          api.adminGetCategories(),
        ])
        if (cancelled) return
        setStats({
          products: products.length,
          categories: categories.length,
          active: products.filter((p) => p.isActive).length,
        })
      } catch (err) {
        if (!cancelled) setError(err.message || 'Unable to load dashboard.')
      }
    }

    load()
    return () => {
      cancelled = true
    }
  }, [])

  return (
    <div className="admin-page">
      <div className="admin-page-head">
        <div>
          <p className="eyebrow">Overview</p>
          <h1>Dashboard</h1>
        </div>
      </div>

      {error ? <p className="admin-error">{error}</p> : null}

      <div className="admin-stats">
        <div className="admin-stat">
          <span>Products</span>
          <strong>{stats.products}</strong>
        </div>
        <div className="admin-stat">
          <span>Active</span>
          <strong>{stats.active}</strong>
        </div>
        <div className="admin-stat">
          <span>Categories</span>
          <strong>{stats.categories}</strong>
        </div>
      </div>

      <div className="admin-actions-row">
        <Link to="/admin/products" className="btn btn-primary">
          Manage products
        </Link>
        <Link to="/admin/categories" className="btn btn-secondary">
          Manage categories
        </Link>
        <Link to="/admin/products/new" className="btn btn-brass">
          Add product
        </Link>
        <Link to="/admin/reports" className="btn btn-secondary">
          View reports
        </Link>
      </div>
    </div>
  )
}
