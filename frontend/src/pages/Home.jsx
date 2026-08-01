import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import ProductCard from '../components/ProductCard'
import { api } from '../api/client'

export default function Home() {
  const [showcase, setShowcase] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  useEffect(() => {
    let cancelled = false

    async function load() {
      setLoading(true)
      setError('')
      try {
        const products = await api.getProducts()
        if (cancelled) return
        const featured = products.filter((p) => p.badge).slice(0, 3)
        const rest = products.filter((p) => !p.badge).slice(0, 3)
        setShowcase([...featured, ...rest].slice(0, 6))
      } catch (err) {
        if (!cancelled) setError(err.message || 'Unable to load products.')
      } finally {
        if (!cancelled) setLoading(false)
      }
    }

    load()
    return () => {
      cancelled = true
    }
  }, [])

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
          <h1 className="hero-headline">Bags built for the life you carry.</h1>
          <p className="hero-copy">
            Premium totes, packs, and travel bags — thoughtfully made for daily rhythm and long journeys.
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
          <span>Leather totes</span>
          <span>Daypacks</span>
          <span>Weekenders</span>
          <span>Work briefs</span>
          <span>Crossbody</span>
          <span>Travel ready</span>
          <span>Leather totes</span>
          <span>Daypacks</span>
          <span>Weekenders</span>
          <span>Work briefs</span>
          <span>Crossbody</span>
          <span>Travel ready</span>
        </div>
      </div>

      <section className="section">
        <div className="container">
          <div className="section-head">
            <div>
              <span className="eyebrow">Featured</span>
              <h2>Carry well. Travel light.</h2>
            </div>
          </div>

          {loading ? <p>Loading bags from API…</p> : null}
          {error ? <p style={{ color: 'var(--danger)' }}>{error}</p> : null}

          <div className="product-grid">
            {showcase.map((product) => (
              <ProductCard key={product.id} product={product} />
            ))}
          </div>

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
