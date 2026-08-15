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
            <p>
              School bags built for Boys, Girls, and Kids — comfortable, durable packs for the daily walk to
              class and everything after.
            </p>
          </div>

          <div>
            <h4>Shop</h4>
            <ul>
              <li>
                <Link to="/shop?category=school-bags">All school bags</Link>
              </li>
              <li>
                <Link to="/shop?category=school-bags&subCategory=boys">Boys</Link>
              </li>
              <li>
                <Link to="/shop?category=school-bags&subCategory=girls">Girls</Link>
              </li>
              <li>
                <Link to="/shop?category=school-bags&subCategory=kids">Kids</Link>
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
              <li>
                <Link to="/business">Sign up as seller</Link>
              </li>
              <li>
                <Link to="/business/login">Seller login</Link>
              </li>
            </ul>
          </div>

          <div>
            <h4>Support</h4>
            <ul>
              <li>Free shipping over ₹2,500</li>
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
