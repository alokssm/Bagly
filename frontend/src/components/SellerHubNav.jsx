import { NavLink } from 'react-router-dom'

export default function SellerHubNav() {
  return (
    <nav className="seller-hub-nav" aria-label="Seller hub">
      <NavLink to="/seller" end>
        Profile
      </NavLink>
      <NavLink to="/seller/products">Products</NavLink>
      <NavLink to="/seller/orders">Orders</NavLink>
      <NavLink to="/seller/pickups">Pickups</NavLink>
    </nav>
  )
}
