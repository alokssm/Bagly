import { useCallback, useEffect, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import ProductCard from '../components/ProductCard'
import ProductSearchBar from '../components/ProductSearchBar'
import LoadingState from '../components/LoadingState'
import ApiErrorState from '../components/ApiErrorState'
import { api } from '../api/client'

export default function Shop() {
  const [params, setParams] = useSearchParams()
  const activeCategory = params.get('category') || 'all'
  const activeSubCategory = params.get('subCategory') || 'all'
  const qParam = (params.get('q') || '').trim()
  const [sort, setSort] = useState('featured')
  const [categories, setCategories] = useState([{ id: 'all', label: 'All bags' }])
  const [products, setProducts] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [searchInput, setSearchInput] = useState(qParam)
  const lastPushedQ = useRef(qParam)

  const topLevelCategories = categories.filter((cat) => !cat.parentId)
  const subCategories = categories.filter((cat) => cat.parentId === activeCategory)

  useEffect(() => {
    let cancelled = false

    async function loadCategories() {
      try {
        const data = await api.getCategories()
        if (!cancelled && Array.isArray(data) && data.length) {
          setCategories(data)
        }
      } catch {
        // Category filters fall back to "All bags" — product load shows the error.
      }
    }

    loadCategories()
    return () => {
      cancelled = true
    }
  }, [])

  // Sync input when `q` changes from outside this page (Home search, back/forward).
  useEffect(() => {
    if (qParam === lastPushedQ.current) return
    lastPushedQ.current = qParam
    setSearchInput(qParam)
  }, [qParam])

  // Debounced live filter: keep the address bar shareable via `?q=`.
  useEffect(() => {
    const handle = setTimeout(() => {
      const trimmed = searchInput.trim()
      if (trimmed === lastPushedQ.current) return
      lastPushedQ.current = trimmed
      setParams(
        (prev) => {
          const next = new URLSearchParams(prev)
          if (trimmed) next.set('q', trimmed)
          else next.delete('q')
          return next
        },
        { replace: true },
      )
    }, 300)
    return () => clearTimeout(handle)
  }, [searchInput, setParams])

  const loadProducts = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      const data = await api.getProducts({
        category: activeCategory,
        subCategory: activeSubCategory,
        sort,
        q: qParam || undefined,
      })
      setProducts(data)
    } catch (err) {
      setProducts([])
      setError(err.message || 'Unable to load products.')
    } finally {
      setLoading(false)
    }
  }, [activeCategory, activeSubCategory, sort, qParam])

  useEffect(() => {
    loadProducts()
  }, [loadProducts])

  const setCategory = (id) => {
    const next = new URLSearchParams(params)
    if (id === 'all') next.delete('category')
    else next.set('category', id)
    next.delete('subCategory')
    setParams(next)
  }

  const setSubCategory = (id) => {
    const next = new URLSearchParams(params)
    if (id === 'all') next.delete('subCategory')
    else next.set('subCategory', id)
    setParams(next)
  }

  const handleSearchSubmit = (term) => {
    const trimmed = term.trim()
    if (trimmed === lastPushedQ.current) return
    lastPushedQ.current = trimmed
    setSearchInput(trimmed)
    const next = new URLSearchParams(params)
    if (trimmed) next.set('q', trimmed)
    else next.delete('q')
    setParams(next, { replace: true })
  }

  return (
    <section className="section" style={{ paddingTop: 0 }}>
      <div className="container">
        <div className="page-hero">
          <span className="eyebrow">Shop</span>
          <h1>The Bagly collection</h1>
          <p>Filter by style and find the bag that fits your day — from soft totes to structured travel.</p>
          <ProductSearchBar
            id="shop-product-search"
            value={searchInput}
            onChange={setSearchInput}
            onSubmit={handleSearchSubmit}
            placeholder="Search by name or style…"
            className="product-search--shop"
          />
        </div>

        <div className="filters" role="tablist" aria-label="Categories">
          {topLevelCategories.map((cat) => (
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

        {subCategories.length ? (
          <div className="filters filters-sub" role="tablist" aria-label="Subcategories">
            <button
              type="button"
              className={`filter-chip filter-chip-sub ${activeSubCategory === 'all' ? 'active' : ''}`}
              onClick={() => setSubCategory('all')}
            >
              All
            </button>
            {subCategories.map((cat) => (
              <button
                key={cat.id}
                type="button"
                className={`filter-chip filter-chip-sub ${activeSubCategory === cat.id ? 'active' : ''}`}
                onClick={() => setSubCategory(cat.id)}
              >
                {cat.label}
              </button>
            ))}
          </div>
        ) : null}

        <div className="shop-toolbar">
          <p>
            {loading ? (
              'Loading products…'
            ) : error ? (
              'Unable to load products'
            ) : (
              <>
                Showing <strong>{products.length}</strong> bag{products.length === 1 ? '' : 's'}
                {qParam ? (
                  <>
                    {' '}
                    for <strong>&ldquo;{qParam}&rdquo;</strong>
                  </>
                ) : null}
              </>
            )}
          </p>
          <label>
            Sort{' '}
            <select value={sort} onChange={(e) => setSort(e.target.value)} disabled={loading}>
              <option value="featured">Featured</option>
              <option value="price-asc">Price: low to high</option>
              <option value="price-desc">Price: high to low</option>
              <option value="name">Name</option>
            </select>
          </label>
        </div>

        {loading ? <LoadingState message="Loading products…" compact /> : null}

        {!loading && error ? (
          <ApiErrorState title="Couldn't load products" message={error} onRetry={loadProducts} compact />
        ) : null}

        {!loading && !error && products.length === 0 ? (
          <p className="shop-empty" role="status">
            {qParam ? `No products match “${qParam}”.` : 'No products found.'}
          </p>
        ) : null}

        {!loading && !error && products.length > 0 ? (
          <div className="product-grid">
            {products.map((product) => (
              <ProductCard key={product.id} product={product} />
            ))}
          </div>
        ) : null}
      </div>
    </section>
  )
}
