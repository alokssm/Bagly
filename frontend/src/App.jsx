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
import Home from './pages/Home'
import Shop from './pages/Shop'
import ProductDetail from './pages/ProductDetail'
import Cart from './pages/Cart'
import Checkout from './pages/Checkout'
import Orders from './pages/Orders'
import Profile from './pages/Profile'
import Addresses from './pages/Addresses'
import About from './pages/About'
import Login from './pages/Login'
import Register from './pages/Register'
import AdminLogin from './pages/admin/AdminLogin'
import AdminDashboard from './pages/admin/AdminDashboard'
import AdminProducts from './pages/admin/AdminProducts'
import AdminProductForm from './pages/admin/AdminProductForm'
import AdminCategories from './pages/admin/AdminCategories'
import AdminOrders from './pages/admin/AdminOrders'
import AdminAnalytics from './pages/admin/AdminAnalytics'
import AdminReports from './pages/admin/AdminReports'

function StoreLayout() {
  const { isAuthenticated, token } = useCustomerAuth()
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
      <AuthProvider>
        <CustomerAuthProvider>
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
                <Route path="/login" element={<Login />} />
                <Route path="/register" element={<Register />} />
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
                <Route path="products/new" element={<AdminProductForm />} />
                <Route path="products/:id/edit" element={<AdminProductForm />} />
                <Route path="categories" element={<AdminCategories />} />
                <Route path="orders" element={<AdminOrders />} />
                <Route path="analytics" element={<AdminAnalytics />} />
                <Route path="reports" element={<AdminReports />} />
              </Route>

              <Route path="*" element={<Navigate to="/" replace />} />
            </Routes>
          </CartProvider>
        </CustomerAuthProvider>
      </AuthProvider>
    </BrowserRouter>
  )
}
