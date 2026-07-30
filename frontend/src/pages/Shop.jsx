import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import ProductCard from '../components/ProductCard'
import { api } from '../api/client'

export default function Shop() {
  const [params, setParams] = useSearchParams()
  const activeCategory = params.get('category') || 'all'
  const [sort, setSort] = useState('featured')
  const [categories, setCategories] = useState([{ id: 'all', label: 'All bags' }])
  const [products, setProducts] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  useEffect(() => {
    let cancelled = false

    async function loadCategories() {
      try {
        const data = await api.getCategories()
        if (!cancelled && Array.isArray(data) && data.length) {
          setCategories(data)
        }
      } catch (err) {
        if (!cancelled) setError(err.message || 'Unable to load categories.')
      }
    }

    loadCategories()
    return () => {
      cancelled = true
    }
  }, [])

  useEffect(() => {
    let cancelled = false

    async function loadProducts() {
      setLoading(true)
      setError('')
      try {
        const data = await api.getProducts({
          category: activeCategory,
          sort,
        })
        if (!cancelled) setProducts(data)
      } catch (err) {
        if (!cancelled) {
          setProducts([])
          setError(err.message || 'Unable to load products from API.')
        }
      } finally {
        if (!cancelled) setLoading(false)
      }
    }

    loadProducts()
    return () => {
      cancelled = true
    }
  }, [activeCategory, sort])

  const setCategory = (id) => {
    const next = new URLSearchParams(params)
    if (id === 'all') next.delete('category')
    else next.set('category', id)
    setParams(next)
  }

  return (
    <section className="section" style={{ paddingTop: 0 }}>
      <div className="container">
        <div className="page-hero">
          <span className="eyebrow">Shop</span>
          <h1>The Bagly collection</h1>
          <p>Filter by style and find the bag that fits your day — from soft totes to structured travel.</p>
        </div>

        <div className="filters" role="tablist" aria-label="Categories">
          {categories.map((cat) => (
            <button
              key={cat.id}
              type="button"
              className={`filter-chip ${activeCategory === cat.id ? 'active' : ''}`}
              onClick={() => setCategory(cat.id)}
            >
              {cat.label}
            </button>
          ))}
        </div>

        <div className="shop-toolbar">
          <p>
            {loading ? (
              'Loading bags from API…'
            ) : (
              <>
                Showing <strong>{products.length}</strong> bag{products.length === 1 ? '' : 's'}
              </>
            )}
          </p>
          <label>
            Sort{' '}
            <select value={sort} onChange={(e) => setSort(e.target.value)}>
              <option value="featured">Featured</option>
              <option value="price-asc">Price: low to high</option>
              <option value="price-desc">Price: high to low</option>
              <option value="name">Name</option>
            </select>
          </label>
        </div>

        {error ? <p style={{ color: 'var(--danger)', marginBottom: '1rem' }}>{error}</p> : null}

        <div className="product-grid">
          {products.map((product) => (
            <ProductCard key={product.id} product={product} />
          ))}
        </div>
      </div>
    </section>
  )
}
