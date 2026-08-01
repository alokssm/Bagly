import { Link } from 'react-router-dom'

export default function Footer() {
  return (
    <footer className="footer">
      <div className="container">
        <div className="footer-grid">
          <div>
            <div className="footer-brand">
              Bag<span>ly</span>
            </div>
            <p>Curated bags for everyday carry, travel, and work — designed to last seasons, not weeks.</p>
          </div>

          <div>
            <h4>Shop</h4>
            <ul>
              <li>
                <Link to="/shop">All bags</Link>
              </li>
              <li>
                <Link to="/shop?category=tote">Totes</Link>
              </li>
              <li>
                <Link to="/shop?category=backpack">Backpacks</Link>
              </li>
              <li>
                <Link to="/shop?category=travel">Travel</Link>
              </li>
            </ul>
          </div>

          <div>
            <h4>Company</h4>
            <ul>
              <li>
                <Link to="/about">Our story</Link>
              </li>
              <li>
                <a href="mailto:hello@bagly.store">Contact</a>
              </li>
            </ul>
          </div>

          <div>
            <h4>Support</h4>
            <ul>
              <li>Free shipping over ₹12,999</li>
              <li>30-day returns</li>
              <li>Lifetime repair program</li>
            </ul>
          </div>
        </div>

        <div className="footer-bottom">
          <span>© {new Date().getFullYear()} Bagly. All rights reserved.</span>
          <span>Powered by .NET 8 API</span>
        </div>
      </div>
    </footer>
  )
}
