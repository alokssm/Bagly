import { useCallback, useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import ProductCard from '../components/ProductCard'
import ProductSearchBar from '../components/ProductSearchBar'
import LoadingState from '../components/LoadingState'
import ApiErrorState from '../components/ApiErrorState'
import { api } from '../api/client'

const CATEGORY_TILES = [
  {
    id: 'boys',
    label: 'Boys',
    copy: 'Tough packs for the school run',
    to: '/shop?category=school-bags&subCategory=boys',
    image:
      'https://images.unsplash.com/photo-1553062407-98eeb64c6a62?auto=format&fit=crop&w=900&q=80',
  },
  {
    id: 'girls',
    label: 'Girls',
    copy: 'Light, roomy, ready for class',
    to: '/shop?category=school-bags&subCategory=girls',
    image:
      'https://images.unsplash.com/photo-1581605405669-fcdf81165afa?auto=format&fit=crop&w=900&q=80',
  },
  {
    id: 'kids',
    label: 'Kids',
    copy: 'Comfortable carry for little ones',
    to: '/shop?category=school-bags&subCategory=kids',
    image:
      'https://images.unsplash.com/photo-1622560480605-d83c853bc5c3?auto=format&fit=crop&w=900&q=80',
  },
]

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
            src="https://images.unsplash.com/photo-1553062407-98eeb64c6a62?auto=format&fit=crop&w=2000&q=80"
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
            <Link to="/about" className="btn btn-ghost-hero">
              Our craft
            </Link>
          </div>
        </div>
      </section>

      <section className="section category-section">
        <div className="container">
          <div className="section-head">
            <div>
              <span className="eyebrow">Shop by</span>
              <h2>Find their school bag</h2>
            </div>
            <p>Browse Boys, Girls, and Kids collections — or see every school bag in the shop.</p>
          </div>

          <div className="category-tile-grid">
            {CATEGORY_TILES.map((tile) => (
              <Link key={tile.id} to={tile.to} className="category-tile">
                <span className="category-tile-media" aria-hidden="true">
                  <img src={tile.image} alt="" loading="lazy" />
                </span>
                <span className="category-tile-body">
                  <span className="category-tile-label">{tile.label}</span>
                  <span className="category-tile-copy">{tile.copy}</span>
                </span>
              </Link>
            ))}
          </div>

          <div className="category-section-cta">
            <Link to="/shop?category=school-bags" className="btn btn-secondary">
              All school bags
            </Link>
          </div>
        </div>
      </section>

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

          <div className="home-browse-cta">
            <Link to="/shop" className="btn btn-primary">
              Browse all bags
            </Link>
          </div>
        </div>
      </section>
    </>
  )
}
