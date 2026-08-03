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
                <Link to="/contact">Contact</Link>
              </li>
            </ul>
          </div>

          <div>
            <h4>Support</h4>
            <ul>
              <li>Free shipping over ₹10000</li>
              <li>10-day returns</li>
            </ul>
          </div>
        </div>

        <div className="footer-bottom">
          <span>© {new Date().getFullYear()} Bagly. All rights reserved.</span>
        </div>
      </div>
    </footer>
  )
}
