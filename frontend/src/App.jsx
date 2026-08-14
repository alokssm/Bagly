import { BrowserRouter, Navigate, Outlet, Route, Routes } from 'react-router-dom'
import Navbar from './components/Navbar'
import Footer from './components/Footer'
import ChatWidget from './components/ChatWidget'
import InstallPrompt from './components/InstallPrompt'
import AdminLayout from './components/AdminLayout'
import ProtectedRoute from './components/ProtectedRoute'
import CustomerRoute from './components/CustomerRoute'
import { AuthProvider } from './context/AuthContext'
import { CartProvider } from './context/CartContext'
import { CustomerAuthProvider, useCustomerAuth } from './context/CustomerAuthContext'
import { SellerAuthProvider } from './context/SellerAuthContext'
import Home from './pages/Home'
import Shop from './pages/Shop'
import ProductDetail from './pages/ProductDetail'
import Cart from './pages/Cart'
import Checkout from './pages/Checkout'
import Orders from './pages/Orders'
import Profile from './pages/Profile'
import Addresses from './pages/Addresses'
import About from './pages/About'
import Contact from './pages/Contact'
import Login from './pages/Login'
import Register from './pages/Register'
import Business from './pages/Business'
import BusinessLogin from './pages/BusinessLogin'
import SellerDashboard from './pages/SellerDashboard'
import SellerProducts from './pages/SellerProducts'
import SellerProductForm from './pages/SellerProductForm'
import SellerPickups from './pages/SellerPickups'
import SellerRoute from './components/SellerRoute'
import AdminLogin from './pages/admin/AdminLogin'
import AdminDashboard from './pages/admin/AdminDashboard'
import AdminProducts from './pages/admin/AdminProducts'
import AdminCategories from './pages/admin/AdminCategories'
import AdminOrders from './pages/admin/AdminOrders'
import AdminShipping from './pages/admin/shipping/AdminShipping'
import AdminSellers from './pages/admin/AdminSellers'
import AdminAnalytics from './pages/admin/AdminAnalytics'
import AdminTraffic from './pages/admin/AdminTraffic'
import AdminReports from './pages/admin/AdminReports'
import usePageViewTracking from './hooks/usePageViewTracking'
import ScrollToTop from './components/ScrollToTop'

function StoreLayout() {
  const { isAuthenticated, token } = useCustomerAuth()
  usePageViewTracking()
  return (
    <div className="app-shell">
      <InstallPrompt />
      <Navbar />
      <main className="main">
        <Outlet />
      </main>
      <Footer />
      {isAuthenticated ? <ChatWidget token={token} /> : null}
    </div>
  )
}

export default function App() {
  return (
    <BrowserRouter>
      <ScrollToTop />
      <AuthProvider>
        <CustomerAuthProvider>
          <SellerAuthProvider>
            <CartProvider>
              <Routes>
                <Route element={<StoreLayout />}>
                  <Route path="/" element={<Home />} />
                  <Route path="/shop" element={<Shop />} />
                  <Route path="/product/:id" element={<ProductDetail />} />
                  <Route path="/cart" element={<Cart />} />
                  <Route path="/checkout" element={<Checkout />} />
                  <Route
                    path="/orders"
                    element={
                      <CustomerRoute>
                        <Orders />
                      </CustomerRoute>
                    }
                  />
                  <Route
                    path="/profile"
                    element={
                      <CustomerRoute>
                        <Profile />
                      </CustomerRoute>
                    }
                  />
                  <Route
                    path="/addresses"
                    element={
                      <CustomerRoute>
                        <Addresses />
                      </CustomerRoute>
                    }
                  />
                  <Route path="/about" element={<About />} />
                  <Route path="/contact" element={<Contact />} />
                  <Route path="/login" element={<Login />} />
                  <Route path="/register" element={<Register />} />
                  <Route path="/business" element={<Business />} />
                  <Route path="/business/login" element={<BusinessLogin />} />
                  <Route
                    path="/seller"
                    element={
                      <SellerRoute>
                        <SellerDashboard />
                      </SellerRoute>
                    }
                  />
                  <Route
                    path="/seller/products"
                    element={
                      <SellerRoute>
                        <SellerProducts />
                      </SellerRoute>
                    }
                  />
                  <Route
                    path="/seller/products/new"
                    element={
                      <SellerRoute>
                        <SellerProductForm />
                      </SellerRoute>
                    }
                  />
                  <Route
                    path="/seller/products/:id/edit"
                    element={
                      <SellerRoute>
                        <SellerProductForm />
                      </SellerRoute>
                    }
                  />
                  <Route
                    path="/seller/pickups"
                    element={
                      <SellerRoute>
                        <SellerPickups />
                      </SellerRoute>
                    }
                  />
                </Route>

                <Route path="/admin/login" element={<AdminLogin />} />
                <Route
                  path="/admin"
                  element={
                    <ProtectedRoute>
                      <AdminLayout />
                    </ProtectedRoute>
                  }
                >
                  <Route index element={<AdminDashboard />} />
                  <Route path="products" element={<AdminProducts />} />
                  <Route path="products/new" element={<Navigate to="/admin/products" replace />} />
                  <Route path="products/:id/edit" element={<Navigate to="/admin/products" replace />} />
                  <Route path="categories" element={<AdminCategories />} />
                  <Route path="orders" element={<AdminOrders />} />
                  <Route path="shipping" element={<AdminShipping />} />
                  <Route path="sellers" element={<AdminSellers />} />
                  <Route path="analytics" element={<AdminAnalytics />} />
                  <Route path="traffic" element={<AdminTraffic />} />
                  <Route path="reports" element={<AdminReports />} />
                </Route>

                <Route path="*" element={<Navigate to="/" replace />} />
              </Routes>
            </CartProvider>
          </SellerAuthProvider>
        </CustomerAuthProvider>
      </AuthProvider>
    </BrowserRouter>
  )
}
