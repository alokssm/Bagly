const TOKEN_KEY = 'bagly-admin-token'
const CUSTOMER_TOKEN_KEY = 'bagly-customer-token'
const SELLER_TOKEN_KEY = 'bagly-seller-token'

/**
 * - Cloud (Vercel → Render): use VITE_API_URL as-is (e.g. https://bagly.onrender.com/api)
 * - Local LAN/IIS: if both page and API are localhost/IP, keep API port but use page hostname
 */
function resolveApiBase() {
  const configured = import.meta.env.VITE_API_URL || '/api'
  if (typeof window === 'undefined' || !window.location?.hostname) {
    return configured
  }

  const isLocalHost = (host) =>
    host === 'localhost' ||
    host === '127.0.0.1' ||
    /^\d{1,3}(\.\d{1,3}){3}$/.test(host)

  try {
    if (configured.startsWith('http://') || configured.startsWith('https://')) {
      const url = new URL(configured)
      const pageHost = window.location.hostname
      if (isLocalHost(pageHost) && isLocalHost(url.hostname)) {
        url.hostname = pageHost
      }
      return url.toString().replace(/\/$/, '')
    }
  } catch {
    // fall through
  }

  return configured
}

function getApiBase() {
  return resolveApiBase()
}

export const NETWORK_ERROR_MESSAGE =
  "We're having trouble reaching Bagly right now. Please try again in a moment."

export function isNetworkError(err) {
  return Boolean(err?.network)
}

function createNetworkError() {
  const error = new Error(NETWORK_ERROR_MESSAGE)
  error.network = true
  return error
}

/**
 * Derives the SignalR hub base URL from the API base.
 * - Absolute (e.g. https://x.com/api) → https://x.com/hubs
 * - Relative (dev proxy, default /api) → /hubs (Vite proxies this to the API host)
 */
export function getHubBase() {
  const apiBase = getApiBase()
  if (apiBase.startsWith('http://') || apiBase.startsWith('https://')) {
    return apiBase.replace(/\/api\/?$/, '') + '/hubs'
  }
  return '/hubs'
}

export function getAuthToken() {
  return localStorage.getItem(TOKEN_KEY)
}

export function setAuthToken(token) {
  if (token) localStorage.setItem(TOKEN_KEY, token)
  else localStorage.removeItem(TOKEN_KEY)
}

export function getCustomerToken() {
  return localStorage.getItem(CUSTOMER_TOKEN_KEY)
}

export function setCustomerToken(token) {
  if (token) localStorage.setItem(CUSTOMER_TOKEN_KEY, token)
  else localStorage.removeItem(CUSTOMER_TOKEN_KEY)
}

export function getSellerToken() {
  return localStorage.getItem(SELLER_TOKEN_KEY)
}

export function setSellerToken(token) {
  if (token) localStorage.setItem(SELLER_TOKEN_KEY, token)
  else localStorage.removeItem(SELLER_TOKEN_KEY)
}

function extractErrorMessage(data, status) {
  if (!data || typeof data !== 'object') return `Request failed (${status})`
  if (data.message) return data.message
  if (data.title && data.errors) {
    const details = Object.entries(data.errors)
      .flatMap(([field, messages]) => {
        const list = Array.isArray(messages) ? messages : [messages]
        return list.map((m) => `${field}: ${m}`)
      })
      .join(' | ')
    return details || data.title
  }
  if (data.title) return data.title
  return `Request failed (${status})`
}

async function request(path, options = {}) {
  const { body, headers, auth = false, ...rest } = options
  const token =
    auth === 'customer' ? getCustomerToken() : auth === 'seller' ? getSellerToken() : getAuthToken()
  const apiBase = getApiBase()

  let response
  try {
    response = await fetch(`${apiBase}${path}`, {
      headers: {
        Accept: 'application/json',
        ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
        ...(auth && token ? { Authorization: `Bearer ${token}` } : {}),
        ...headers,
      },
      body: body !== undefined ? JSON.stringify(body) : undefined,
      ...rest,
    })
  } catch (err) {
    if (err?.network) throw err
    throw createNetworkError()
  }

  if (!response.ok) {
    let message = `Request failed (${response.status})`
    try {
      const data = await response.json()
      message = extractErrorMessage(data, response.status)
    } catch {
      // ignore
    }
    const error = new Error(message)
    error.status = response.status
    throw error
  }

  if (response.status === 204) return null
  const text = await response.text()
  return text ? JSON.parse(text) : null
}

/** Public Google Identity Services config (client ID is safe to expose). */
export function getGoogleAuthConfig() {
  return request('/auth/customer/google-config')
}

/** Multipart upload — cannot reuse request() since it always JSON-encodes the body. */
async function requestUpload(path, file, { auth = true } = {}) {
  const token =
    auth === 'customer' ? getCustomerToken() : auth === 'seller' ? getSellerToken() : getAuthToken()
  const apiBase = getApiBase()
  const formData = new FormData()
  formData.append('file', file)

  let response
  try {
    response = await fetch(`${apiBase}${path}`, {
      method: 'POST',
      headers: {
        Accept: 'application/json',
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      body: formData,
    })
  } catch (err) {
    if (err?.network) throw err
    throw createNetworkError()
  }

  if (!response.ok) {
    let message = `Upload failed (${response.status})`
    try {
      const data = await response.json()
      message = extractErrorMessage(data, response.status)
    } catch {
      // ignore
    }
    const error = new Error(message)
    error.status = response.status
    throw error
  }

  const text = await response.text()
  return text ? JSON.parse(text) : null
}

export const api = {
  health: () => request('/health'),

  login: (email, password) =>
    request('/auth/login', {
      method: 'POST',
      body: { email: String(email).trim(), password: String(password) },
    }),

  logout: () => request('/auth/logout', { method: 'POST', auth: true }),

  me: () => request('/auth/me', { auth: true }),

  customerRegister: (name, email, password, confirmPassword) =>
    request('/auth/customer/register', {
      method: 'POST',
      body: { name, email, password, confirmPassword },
    }),

  sellerRegister: (name, businessName, email, phone, password, confirmPassword) =>
    request('/auth/seller/register', {
      method: 'POST',
      body: { name, businessName, email, phone, password, confirmPassword },
    }),

  sellerLogin: (email, password) =>
    request('/auth/seller/login', {
      method: 'POST',
      body: { email, password },
    }),

  sellerMe: () => request('/auth/seller/me', { auth: 'seller' }),

  sellerGetProfile: () => request('/auth/seller/profile', { auth: 'seller' }),

  sellerUpdateProfile: (payload) =>
    request('/auth/seller/profile', {
      method: 'PUT',
      body: payload,
      auth: 'seller',
    }),

  sellerGetProducts: (params = {}) => {
    const qs = new URLSearchParams()
    Object.entries(params).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') qs.set(key, String(value))
    })
    const query = qs.toString()
    return request(`/seller/products${query ? `?${query}` : ''}`, { auth: 'seller' })
  },
  sellerGetProduct: (id) =>
    request(`/seller/products/${encodeURIComponent(id)}`, { auth: 'seller' }),
  sellerCreateProduct: (payload) =>
    request('/seller/products', { method: 'POST', body: payload, auth: 'seller' }),
  sellerUpdateProduct: (id, payload) =>
    request(`/seller/products/${encodeURIComponent(id)}`, {
      method: 'PUT',
      body: payload,
      auth: 'seller',
    }),
  sellerDeleteProduct: (id) =>
    request(`/seller/products/${encodeURIComponent(id)}`, { method: 'DELETE', auth: 'seller' }),
  sellerUploadImage: (file) => requestUpload('/seller/uploads/image', file, { auth: 'seller' }),
  sellerGetShiprocketPickupLocations: () =>
    request('/seller/shiprocket/pickup-locations', { auth: 'seller' }),
  sellerGetPickups: () => request('/seller/pickups', { auth: 'seller' }),
  sellerCreatePickup: (payload) =>
    request('/seller/pickups', { method: 'POST', body: payload, auth: 'seller' }),
  sellerGetOrders: (params = {}) => {
    const qs = new URLSearchParams()
    Object.entries(params).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') qs.set(key, String(value))
    })
    const query = qs.toString()
    return request(`/seller/orders${query ? `?${query}` : ''}`, { auth: 'seller' })
  },
  sellerGetOrdersReport: (params = {}) => {
    const qs = new URLSearchParams()
    Object.entries(params).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') qs.set(key, String(value))
    })
    const query = qs.toString()
    return request(`/seller/orders/report${query ? `?${query}` : ''}`, { auth: 'seller' })
  },
  sellerMarkShipmentReadyToShip: (orderId, shipmentId) =>
    request(`/seller/orders/${encodeURIComponent(orderId)}/shipments/${encodeURIComponent(shipmentId)}/ready-to-ship`, {
      method: 'POST',
      auth: 'seller',
    }),
  sellerCancelOrder: (orderId) =>
    request(`/seller/orders/${encodeURIComponent(orderId)}/cancel`, {
      method: 'POST',
      auth: 'seller',
    }),

  customerLogin: (email, password) =>
    request('/auth/customer/login', {
      method: 'POST',
      body: { email, password },
    }),

  customerGoogleLogin: (idToken) =>
    request('/auth/customer/google', {
      method: 'POST',
      body: { idToken },
    }),

  getGoogleAuthConfig: () => request('/auth/customer/google-config'),

  customerMe: () => request('/auth/customer/me', { auth: 'customer' }),

  updateCustomerProfile: (name) =>
    request('/auth/customer/me', {
      method: 'PATCH',
      body: { name: String(name || '').trim() },
      auth: 'customer',
    }),

  getProducts: ({ category, subCategory, sort, q, page, pageSize } = {}) => {
    const query = new URLSearchParams()
    if (category && category !== 'all') query.set('category', category)
    if (subCategory && subCategory !== 'all') query.set('subCategory', subCategory)
    if (sort && sort !== 'featured') query.set('sort', sort)
    if (q && String(q).trim()) query.set('q', String(q).trim())
    if (page != null) query.set('page', String(page))
    if (pageSize != null) query.set('pageSize', String(pageSize))
    const qs = query.toString()
    return request(`/products${qs ? `?${qs}` : ''}`)
  },

  getProduct: (id) => request(`/products/${encodeURIComponent(id)}`),

  getProductReviews: (productId) =>
    request(`/products/${encodeURIComponent(productId)}/reviews`, { auth: 'customer' }),

  createProductReview: (productId, { rating, comment }) =>
    request(`/products/${encodeURIComponent(productId)}/reviews`, {
      method: 'POST',
      body: {
        rating: Number(rating),
        comment: comment?.trim() ? String(comment).trim() : null,
      },
      auth: 'customer',
    }),

  updateMyProductReview: (productId, { rating, comment }) =>
    request(`/products/${encodeURIComponent(productId)}/reviews/me`, {
      method: 'PUT',
      body: {
        rating: Number(rating),
        comment: comment?.trim() ? String(comment).trim() : null,
      },
      auth: 'customer',
    }),

  deleteMyProductReview: (productId) =>
    request(`/products/${encodeURIComponent(productId)}/reviews/me`, {
      method: 'DELETE',
      auth: 'customer',
    }),

  getCategories: () => request('/categories'),

  createCart: () => request('/cart', { method: 'POST' }),

  getCart: (cartId) => request(`/cart/${encodeURIComponent(cartId)}`),

  addCartItem: (cartId, { productId, color, quantity = 1 }) =>
    request(`/cart/${encodeURIComponent(cartId)}/items`, {
      method: 'POST',
      body: {
        productId: String(productId),
        color: color || null,
        quantity: Math.max(1, Number(quantity) || 1),
      },
    }),

  updateCartItem: (cartId, productId, color, quantity) =>
    request(
      `/cart/${encodeURIComponent(cartId)}/items/${encodeURIComponent(productId)}?color=${encodeURIComponent(color)}`,
      {
        method: 'PUT',
        body: { quantity: Number(quantity) || 0 },
      },
    ),

  removeCartItem: (cartId, productId, color) =>
    request(
      `/cart/${encodeURIComponent(cartId)}/items/${encodeURIComponent(productId)}?color=${encodeURIComponent(color)}`,
      { method: 'DELETE' },
    ),

  clearCart: (cartId) => request(`/cart/${encodeURIComponent(cartId)}`, { method: 'DELETE' }),

  // auth: 'customer' attaches the customer's Bearer token when logged in (so the backend can
  // link the order to their account via CustomerUserId), but is a no-op for guest checkout —
  // request() only adds the header when a customer token actually exists.
  createOrder: (payload) =>
    request('/orders', { method: 'POST', body: payload, auth: 'customer' }),

  getOrder: (id) => request(`/orders/${encodeURIComponent(id)}`),

  getOrders: () => request('/orders'),

  getRazorpayConfig: () => request('/payments/razorpay/config'),

  initiateRazorpayPayment: (payload) =>
    request('/payments/razorpay/initiate', { method: 'POST', body: payload, auth: 'customer' }),

  verifyRazorpayPayment: (payload) =>
    request('/payments/razorpay/verify', { method: 'POST', body: payload, auth: 'customer' }),

  reportRazorpayFailure: (payload) =>
    request('/payments/razorpay/failure', { method: 'POST', body: payload, auth: 'customer' }),

  getMyOrders: () => request('/account/orders', { auth: 'customer' }),

  getMyOrder: (orderNumber) =>
    request(`/account/orders/${encodeURIComponent(orderNumber)}`, { auth: 'customer' }),

  getMyOrderTrack: (orderNumber) =>
    request(`/account/orders/${encodeURIComponent(orderNumber)}/track`, { auth: 'customer' }),

  getShippingAddresses: () => request('/account/addresses', { auth: 'customer' }),

  createShippingAddress: (payload) =>
    request('/account/addresses', { method: 'POST', body: payload, auth: 'customer' }),

  updateShippingAddress: (id, payload) =>
    request(`/account/addresses/${encodeURIComponent(id)}`, {
      method: 'PUT',
      body: payload,
      auth: 'customer',
    }),

  deleteShippingAddress: (id) =>
    request(`/account/addresses/${encodeURIComponent(id)}`, {
      method: 'DELETE',
      auth: 'customer',
    }),

  setDefaultShippingAddress: (id) =>
    request(`/account/addresses/${encodeURIComponent(id)}/default`, {
      method: 'PATCH',
      auth: 'customer',
    }),

  adminGetProducts: (params = {}) => {
    const qs = new URLSearchParams()
    Object.entries(params).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') qs.set(key, String(value))
    })
    const query = qs.toString()
    return request(`/admin/products${query ? `?${query}` : ''}`, { auth: true })
  },
  adminGetProductStats: () => request('/admin/products/stats', { auth: true }),
  adminGetProduct: (id) => request(`/admin/products/${encodeURIComponent(id)}`, { auth: true }),
  adminCreateProduct: (payload) =>
    request('/admin/products', { method: 'POST', body: payload, auth: true }),
  adminUpdateProduct: (id, payload) =>
    request(`/admin/products/${encodeURIComponent(id)}`, {
      method: 'PUT',
      body: payload,
      auth: true,
    }),
  adminDeleteProduct: (id) =>
    request(`/admin/products/${encodeURIComponent(id)}`, { method: 'DELETE', auth: true }),

  /** Uploads an image file to Cloudinary via the backend and returns { url }. */
  adminUploadImage: (file) => requestUpload('/admin/uploads/image', file),

  adminGetCategories: (params = {}) => {
    const qs = new URLSearchParams()
    Object.entries(params).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') qs.set(key, String(value))
    })
    const query = qs.toString()
    return request(`/admin/categories${query ? `?${query}` : ''}`, { auth: true })
  },
  adminCreateCategory: (payload) =>
    request('/admin/categories', { method: 'POST', body: payload, auth: true }),
  adminUpdateCategory: (id, payload) =>
    request(`/admin/categories/${encodeURIComponent(id)}`, {
      method: 'PUT',
      body: payload,
      auth: true,
    }),
  adminDeleteCategory: (id) =>
    request(`/admin/categories/${encodeURIComponent(id)}`, { method: 'DELETE', auth: true }),

  adminGetReportSummary: () => request('/admin/reports/summary', { auth: true }),

  adminGetAuditLogs: (params = {}) => {
    const qs = new URLSearchParams()
    Object.entries(params).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') qs.set(key, String(value))
    })
    const query = qs.toString()
    return request(`/admin/reports/audit-logs${query ? `?${query}` : ''}`, { auth: true })
  },

  adminGetSystemLogs: (params = {}) => {
    const qs = new URLSearchParams()
    Object.entries(params).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') qs.set(key, String(value))
    })
    const query = qs.toString()
    return request(`/admin/reports/system-logs${query ? `?${query}` : ''}`, { auth: true })
  },

  adminShiprocketConnection: () => request('/admin/shiprocket/connection', { auth: true }),
  adminRetryShiprocket: (id) =>
    request(`/admin/orders/${encodeURIComponent(id)}/shiprocket/retry`, { method: 'POST', auth: true }),

  adminGetShippingOrders: (params = {}) => {
    const qs = new URLSearchParams()
    Object.entries(params).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') qs.set(key, String(value))
    })
    const query = qs.toString()
    return request(`/admin/shipping/orders${query ? `?${query}` : ''}`, { auth: true })
  },
  adminShippingReadyToShip: (shipmentId) =>
    request(`/admin/shipping/shipments/${encodeURIComponent(shipmentId)}/ready-to-ship`, {
      method: 'POST',
      auth: true,
    }),
  adminShippingAssignAwb: (shipmentId, payload) =>
    request(`/admin/shipping/shipments/${encodeURIComponent(shipmentId)}/assign-awb`, {
      method: 'POST',
      body: payload,
      auth: true,
    }),
  adminShippingGenerateLabel: (shipmentId) =>
    request(`/admin/shipping/shipments/${encodeURIComponent(shipmentId)}/generate-label`, {
      method: 'POST',
      auth: true,
    }),
  adminShippingRequestPickup: (shipmentId) =>
    request(`/admin/shipping/shipments/${encodeURIComponent(shipmentId)}/request-pickup`, {
      method: 'POST',
      auth: true,
    }),
  adminShippingGenerateManifest: (shipmentId) =>
    request(`/admin/shipping/shipments/${encodeURIComponent(shipmentId)}/generate-manifest`, {
      method: 'POST',
      auth: true,
    }),
  adminGetShippingApiLogs: (params = {}) => {
    const qs = new URLSearchParams()
    Object.entries(params).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') qs.set(key, String(value))
    })
    const query = qs.toString()
    return request(`/admin/shipping/logs${query ? `?${query}` : ''}`, { auth: true })
  },

  adminGetOrders: (params = {}) => {
    const qs = new URLSearchParams()
    Object.entries(params).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') qs.set(key, String(value))
    })
    const query = qs.toString()
    return request(`/admin/orders${query ? `?${query}` : ''}`, { auth: true })
  },

  adminExportOrders: (params = {}) => {
    const qs = new URLSearchParams()
    Object.entries(params).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') qs.set(key, String(value))
    })
    const query = qs.toString()
    return request(`/admin/orders/export${query ? `?${query}` : ''}`, { auth: true })
  },

  adminGetOrder: (id) => request(`/admin/orders/${encodeURIComponent(id)}`, { auth: true }),

  adminGetSellers: (params = {}) => {
    const qs = new URLSearchParams()
    Object.entries(params).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') qs.set(key, String(value))
    })
    const query = qs.toString()
    return request(`/admin/sellers${query ? `?${query}` : ''}`, { auth: true })
  },

  adminGetSeller: (id) => request(`/admin/sellers/${encodeURIComponent(id)}`, { auth: true }),

  adminApproveSeller: (id) =>
    request(`/admin/sellers/${encodeURIComponent(id)}/approve`, { method: 'POST', auth: true }),

  adminRejectSeller: (id, reason) =>
    request(`/admin/sellers/${encodeURIComponent(id)}/reject`, {
      method: 'POST',
      body: { reason: reason || null },
      auth: true,
    }),

  submitContactForm: (payload) =>
    request('/contact', { method: 'POST', body: payload }),

  adminGetAnalytics: (params = {}) => {
    const qs = new URLSearchParams()
    Object.entries(params).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') qs.set(key, String(value))
    })
    const query = qs.toString()
    return request(`/admin/analytics${query ? `?${query}` : ''}`, { auth: true })
  },

  // Fire-and-forget storefront page-view beacon. Never auth'd, never surfaces errors to the caller.
  recordSiteHit: (payload) =>
    request('/analytics/hit', { method: 'POST', body: payload }).catch(() => null),

  adminGetLocationAnalytics: (params = {}) => {
    const qs = new URLSearchParams()
    Object.entries(params).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') qs.set(key, String(value))
    })
    const query = qs.toString()
    return request(`/admin/analytics/locations${query ? `?${query}` : ''}`, { auth: true })
  },
}
