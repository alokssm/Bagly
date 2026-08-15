import { useCallback, useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import ProductCard from '../components/ProductCard'
import LoadingState from '../components/LoadingState'
import ApiErrorState from '../components/ApiErrorState'
import { api } from '../api/client'

const HERO_SLIDES = [
  {
    id: 'everyday',
    image:
      'https://images.unsplash.com/photo-1553062407-98eeb64c6a62?auto=format&fit=crop&w=2000&q=80',
    headline: 'School bags built for every school day.',
    copy: 'Durable, comfortable backpacks for Boys, Girls, and Kids.',
    cta: { to: '/shop', label: 'Shop the collection' },
    secondary: { to: '/about', label: 'Our craft' },
  },
  {
    id: 'trail',
    image:
      'https://images.unsplash.com/photo-1504148455328-c376907d081c?auto=format&fit=crop&w=2000&q=80',
    headline: 'Carry light. Go further.',
    copy: 'Outdoor-ready packs with the comfort kids need from gate to classroom.',
    cta: { to: '/shop?category=school-bags', label: 'Explore school bags' },
    secondary: { to: '/shop?category=school-bags&subCategory=boys', label: 'Shop Boys' },
  },
  {
    id: 'craft',
    image:
      'https://images.unsplash.com/photo-1622560480605-d83c853bc5c3?auto=format&fit=crop&w=2000&q=80',
    headline: 'Thoughtful details. Lasting wear.',
    copy: 'Forest tones, brass accents, and packs made for the daily walk to school.',
    cta: { to: '/shop?category=school-bags&subCategory=girls', label: 'Shop Girls' },
    secondary: { to: '/about', label: 'Our story' },
  },
  {
    id: 'kids',
    image:
      'https://images.unsplash.com/photo-1581605405669-fcdf81165afa?auto=format&fit=crop&w=2000&q=80',
    headline: 'Sized for little adventurers.',
    copy: 'Comfortable carry for Kids — roomy enough for books, soft enough for play.',
    cta: { to: '/shop?category=school-bags&subCategory=kids', label: 'Shop Kids' },
    secondary: { to: '/shop', label: 'Browse all' },
  },
]

const HERO_INTERVAL_MS = 5500

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
  const [showcase, setShowcase] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [slideIndex, setSlideIndex] = useState(0)
  const [paused, setPaused] = useState(false)
  const touchStartX = useRef(null)

  const slideCount = HERO_SLIDES.length
  const goTo = useCallback(
    (index) => {
      setSlideIndex(((index % slideCount) + slideCount) % slideCount)
    },
    [slideCount],
  )
  const goPrev = useCallback(() => goTo(slideIndex - 1), [goTo, slideIndex])
  const goNext = useCallback(() => goTo(slideIndex + 1), [goTo, slideIndex])

  useEffect(() => {
    if (paused || slideCount <= 1) return undefined
    const id = window.setInterval(() => {
      setSlideIndex((current) => (current + 1) % slideCount)
    }, HERO_INTERVAL_MS)
    return () => window.clearInterval(id)
  }, [paused, slideCount])

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

  const onTouchStart = (event) => {
    touchStartX.current = event.changedTouches[0]?.clientX ?? null
  }

  const onTouchEnd = (event) => {
    const startX = touchStartX.current
    touchStartX.current = null
    if (startX == null) return
    const endX = event.changedTouches[0]?.clientX ?? startX
    const delta = endX - startX
    if (Math.abs(delta) < 48) return
    if (delta > 0) goPrev()
    else goNext()
  }

  return (
    <>
      <section
        className="hero hero-carousel"
        aria-roledescription="carousel"
        aria-label="Bagly featured banners"
        onMouseEnter={() => setPaused(true)}
        onMouseLeave={() => setPaused(false)}
        onFocusCapture={() => setPaused(true)}
        onBlurCapture={(event) => {
          if (!event.currentTarget.contains(event.relatedTarget)) {
            setPaused(false)
          }
        }}
        onTouchStart={onTouchStart}
        onTouchEnd={onTouchEnd}
      >
        <div className="hero-track">
          {HERO_SLIDES.map((slide, index) => {
            const isActive = index === slideIndex
            return (
              <div
                key={slide.id}
                className={`hero-slide${isActive ? ' is-active' : ''}`}
                role="group"
                aria-roledescription="slide"
                aria-label={`${index + 1} of ${slideCount}`}
                aria-hidden={!isActive}
              >
                <div className="hero-media" aria-hidden="true">
                  <img src={slide.image} alt="" loading={index === 0 ? 'eager' : 'lazy'} />
                </div>
                <div className="hero-content">
                  <p className="hero-brand">
                    Bag<em>ly</em>
                  </p>
                  {isActive ? (
                    <h1 className="hero-headline">{slide.headline}</h1>
                  ) : (
                    <p className="hero-headline">{slide.headline}</p>
                  )}
                  <p className="hero-copy">{slide.copy}</p>
                  <div className="hero-ctas">
                    <Link to={slide.cta.to} className="btn btn-brass" tabIndex={isActive ? 0 : -1}>
                      {slide.cta.label}
                    </Link>
                    <Link
                      to={slide.secondary.to}
                      className="btn btn-ghost-hero"
                      tabIndex={isActive ? 0 : -1}
                    >
                      {slide.secondary.label}
                    </Link>
                  </div>
                </div>
              </div>
            )
          })}
        </div>

        <button
          type="button"
          className="hero-nav hero-nav-prev"
          onClick={goPrev}
          aria-label="Previous slide"
        >
          <svg viewBox="0 0 24 24" aria-hidden="true">
            <path d="M15 5l-7 7 7 7" />
          </svg>
        </button>
        <button
          type="button"
          className="hero-nav hero-nav-next"
          onClick={goNext}
          aria-label="Next slide"
        >
          <svg viewBox="0 0 24 24" aria-hidden="true">
            <path d="M9 5l7 7-7 7" />
          </svg>
        </button>

        <div className="hero-dots" role="tablist" aria-label="Slide indicators">
          {HERO_SLIDES.map((slide, index) => (
            <button
              key={slide.id}
              type="button"
              role="tab"
              className={`hero-dot${index === slideIndex ? ' is-active' : ''}`}
              aria-label={`Go to slide ${index + 1}`}
              aria-selected={index === slideIndex}
              onClick={() => goTo(index)}
            />
          ))}
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
