import { useCallback, useEffect, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import ProductCard from '../components/ProductCard'
import LoadingState from '../components/LoadingState'
import ApiErrorState from '../components/ApiErrorState'
import { api } from '../api/client'

const PAGE_SIZE = 20

export default function Shop() {
  const [params, setParams] = useSearchParams()
  const activeCategory = params.get('category') || 'all'
  const activeSubCategory = params.get('subCategory') || 'all'
  const qParam = (params.get('q') || '').trim()
  const [sort, setSort] = useState('featured')
  const [categories, setCategories] = useState([{ id: 'all', label: 'All bags' }])
  const [products, setProducts] = useState([])
  const [page, setPage] = useState(1)
  const [totalCount, setTotalCount] = useState(0)
  const [hasMore, setHasMore] = useState(false)
  const [loading, setLoading] = useState(true)
  const [loadingMore, setLoadingMore] = useState(false)
  const [error, setError] = useState('')
  const loadMoreRef = useRef(null)
  const loadingMoreRef = useRef(false)

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

  // Reset list when filters / sort / search change.
  useEffect(() => {
    let cancelled = false

    async function loadFirstPage() {
      setLoading(true)
      setError('')
      setProducts([])
      setPage(1)
      setHasMore(false)
      setTotalCount(0)
      try {
        const data = await api.getProducts({
          category: activeCategory,
          subCategory: activeSubCategory,
          sort,
          q: qParam || undefined,
          page: 1,
          pageSize: PAGE_SIZE,
        })
        if (cancelled) return
        const items = Array.isArray(data?.items) ? data.items : []
        const total = Number(data?.totalCount) || 0
        const totalPages = Number(data?.totalPages) || 0
        setProducts(items)
        setTotalCount(total)
        setPage(1)
        setHasMore(totalPages > 1)
      } catch (err) {
        if (cancelled) return
        setProducts([])
        setTotalCount(0)
        setHasMore(false)
        setError(err.message || 'Unable to load products.')
      } finally {
        if (!cancelled) setLoading(false)
      }
    }

    loadFirstPage()
    return () => {
      cancelled = true
    }
  }, [activeCategory, activeSubCategory, sort, qParam])

  const loadMore = useCallback(async () => {
    if (loading || loadingMoreRef.current || !hasMore || error) return
    loadingMoreRef.current = true
    setLoadingMore(true)
    const nextPage = page + 1
    try {
      const data = await api.getProducts({
        category: activeCategory,
        subCategory: activeSubCategory,
        sort,
        q: qParam || undefined,
        page: nextPage,
        pageSize: PAGE_SIZE,
      })
      const items = Array.isArray(data?.items) ? data.items : []
      const totalPages = Number(data?.totalPages) || 0
      const currentPage = Number(data?.page) || nextPage
      setProducts((prev) => {
        const seen = new Set(prev.map((p) => p.id))
        const appended = items.filter((p) => !seen.has(p.id))
        return appended.length ? [...prev, ...appended] : prev
      })
      setTotalCount(Number(data?.totalCount) || 0)
      setPage(currentPage)
      setHasMore(currentPage < totalPages)
    } catch (err) {
      setError(err.message || 'Unable to load more products.')
      setHasMore(false)
    } finally {
      loadingMoreRef.current = false
      setLoadingMore(false)
    }
  }, [loading, hasMore, error, page, activeCategory, activeSubCategory, sort, qParam])

  useEffect(() => {
    const node = loadMoreRef.current
    if (!node || loading || !hasMore) return undefined

    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((entry) => entry.isIntersecting)) {
          loadMore()
        }
      },
      { root: null, rootMargin: '240px 0px', threshold: 0 },
    )
    observer.observe(node)
    return () => observer.disconnect()
  }, [loading, hasMore, loadMore, products.length])

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

  const retry = () => {
    // Re-trigger first-page effect by toggling a no-op via remount of deps — simplest: reload page 1 inline.
    setError('')
    setLoading(true)
    setProducts([])
    setPage(1)
    setHasMore(false)
    api
      .getProducts({
        category: activeCategory,
        subCategory: activeSubCategory,
        sort,
        q: qParam || undefined,
        page: 1,
        pageSize: PAGE_SIZE,
      })
      .then((data) => {
        const items = Array.isArray(data?.items) ? data.items : []
        const total = Number(data?.totalCount) || 0
        const totalPages = Number(data?.totalPages) || 0
        setProducts(items)
        setTotalCount(total)
        setPage(1)
        setHasMore(totalPages > 1)
      })
      .catch((err) => {
        setProducts([])
        setTotalCount(0)
        setHasMore(false)
        setError(err.message || 'Unable to load products.')
      })
      .finally(() => setLoading(false))
  }

  return (
    <section className="section shop-page">
      <div className="container">
        <div className="page-hero">
          <span className="eyebrow">Shop</span>
          <h1>School bags for every day</h1>
          <p>
            Durable backpacks for Boys, Girls, and Kids — filter by collection and find the pack that fits the
            school run.
          </p>
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
            ) : error && products.length === 0 ? (
              'Unable to load products'
            ) : (
              <>
                Showing <strong>{products.length}</strong>
                {totalCount > products.length ? (
                  <>
                    {' '}
                    of <strong>{totalCount}</strong>
                  </>
                ) : null}{' '}
                bag{totalCount === 1 || (totalCount === 0 && products.length === 1) ? '' : 's'}
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

        {!loading && error && products.length === 0 ? (
          <ApiErrorState title="Couldn't load products" message={error} onRetry={retry} compact />
        ) : null}

        {!loading && !error && products.length === 0 ? (
          <p className="shop-empty" role="status">
            {qParam ? `No products match “${qParam}”.` : 'No products found.'}
          </p>
        ) : null}

        {!loading && products.length > 0 ? (
          <>
            <div className="product-grid">
              {products.map((product) => (
                <ProductCard key={product.id} product={product} />
              ))}
            </div>
            <div ref={loadMoreRef} className="shop-load-more" aria-hidden={!hasMore && !loadingMore}>
              {loadingMore ? (
                <p className="shop-load-more__status" role="status">
                  Loading…
                </p>
              ) : null}
            </div>
          </>
        ) : null}
      </div>
    </section>
  )
}
