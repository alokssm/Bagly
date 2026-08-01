export default function About() {
  return (
    <section className="section" style={{ paddingTop: 0 }}>
      <div className="container">
        <div className="page-hero">
          <span className="eyebrow">About</span>
          <h1>Made for the long haul</h1>
          <p>Bagly is a bags-first commerce experience.</p>
        </div>

        <div className="story-grid">
          <div className="story-copy">
            <h2>Materials you can feel. Silhouettes that stay.</h2>
            <p>
              We source full-grain leather, waxed canvas, and recycled nylon from makers who obsess over
              stitch quality and hardware that ages with character — not chrome that chips.
            </p>
            <p>
              Every Bagly piece is designed around real carry: laptop days, weekend trains, and the small
              essentials that never leave your side.
            </p>
          </div>
          <div className="story-media">
            <img
              src="https://images.unsplash.com/photo-1548036328-c9fa89d128fa?auto=format&fit=crop&w=1000&q=80"
              alt="Leather bag craftsmanship"
            />
          </div>
        </div>

        <div className="values">
          <div className="value-item">
            <h3>Durable by design</h3>
            <p>Reinforced stress points, thoughtful pockets, and finishes that improve with use.</p>
          </div>
          <div className="value-item">
            <h3>Responsible sourcing</h3>
            <p>Traceable materials and partners who share our standard for longevity over trend.</p>
          </div>
          <div className="value-item">
            <h3>SaaS-ready commerce</h3>
            <p>This React storefront is built to connect to a .NET 8 backend for catalogs, carts, and orders.</p>
          </div>
        </div>
      </div>
    </section>
  )
}
