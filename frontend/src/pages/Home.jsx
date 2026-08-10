import { useCallback, useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import ProductCard from '../components/ProductCard'
import ProductSearchBar from '../components/ProductSearchBar'
import LoadingState from '../components/LoadingState'
import ApiErrorState from '../components/ApiErrorState'
import { api } from '../api/client'

export default function Home() {
  const navigate = useNavigate()
  const [showcase, setShowcase] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [searchQuery, setSearchQuery] = useState('')

  const load = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      const products = await api.getProducts({ category: 'school-bags' })
      const featured = products.filter((p) => p.badge).slice(0, 3)
      const rest = products.filter((p) => !p.badge).slice(0, 3)
      setShowcase([...featured, ...rest].slice(0, 6))
    } catch (err) {
      setError(err.message || 'Unable to load products.')
      setShowcase([])
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    load()
  }, [load])

  const handleSearch = (term) => {
    const q = (term || searchQuery).trim()
    if (!q) {
      navigate('/shop')
      return
    }
    navigate(`/shop?q=${encodeURIComponent(q)}`)
  }

  return (
    <>
      <section className="hero">
        <div className="hero-media" aria-hidden="true">
          <img
            src="https://images.unsplash.com/photo-1591561954557-26941169b49e?auto=format&fit=crop&w=1800&q=80"
            alt=""
          />
        </div>
        <div className="hero-content">
          <p className="hero-brand">
            Bag<em>ly</em>
          </p>
          <h1 className="hero-headline">School bags built for every school day.</h1>
          <p className="hero-copy">
            Durable, comfortable backpacks for Boys, Girls, and Kids — thoughtfully made for the daily walk to
            school and everything in between.
          </p>
          <div className="hero-ctas">
            <Link to="/shop" className="btn btn-brass">
              Shop the collection
            </Link>
            <Link to="/about" className="btn btn-secondary">
              Our craft
            </Link>
          </div>
        </div>
      </section>

      <div className="collections" aria-hidden="true">
        <div className="marquee">
          <span>School Bags</span>
          <span>Boys</span>
          <span>Girls</span>
          <span>Kids</span>
          <span>Back to school</span>
          <span>Study essentials</span>
          <span>School Bags</span>
          <span>Boys</span>
          <span>Girls</span>
          <span>Kids</span>
          <span>Back to school</span>
          <span>Study essentials</span>
        </div>
      </div>

      <section className="section">
        <div className="container">
          <div className="section-head">
            <div>
              <span className="eyebrow">Featured</span>
              <h2>School Bags for Boys, Girls &amp; Kids</h2>
            </div>
          </div>

          <ProductSearchBar
            id="home-product-search"
            value={searchQuery}
            onChange={setSearchQuery}
            onSubmit={handleSearch}
            placeholder="Search the collection…"
            className="product-search--home"
          />

          {loading ? <LoadingState message="Loading products…" compact /> : null}
          {!loading && error ? (
            <ApiErrorState
              title="Couldn't load products"
              message={error}
              onRetry={load}
              compact
            />
          ) : null}

          {!loading && !error ? (
            <div className="product-grid">
              {showcase.map((product) => (
                <ProductCard key={product.id} product={product} />
              ))}
            </div>
          ) : null}

          <div style={{ marginTop: '2.5rem', textAlign: 'center' }}>
            <Link to="/shop" className="btn btn-primary">
              Browse all bags
            </Link>
          </div>
        </div>
      </section>
    </>
  )
}
