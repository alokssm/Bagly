const TOKEN_KEY = 'bagly-admin-token'

/**
 * Prefer same hostname as the page (LAN IP or public IP), keep API port from VITE_API_URL.
 * Example: page http://192.168.1.6:8080 + env http://x:8081/api -> http://192.168.1.6:8081/api
 */
function resolveApiBase() {
  const configured = import.meta.env.VITE_API_URL || '/api'
  if (typeof window === 'undefined' || !window.location?.hostname) {
    return configured
  }

  try {
    if (configured.startsWith('http://') || configured.startsWith('https://')) {
      const url = new URL(configured)
      url.hostname = window.location.hostname
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

export function getAuthToken() {
  return localStorage.getItem(TOKEN_KEY)
}

export function setAuthToken(token) {
  if (token) localStorage.setItem(TOKEN_KEY, token)
  else localStorage.removeItem(TOKEN_KEY)
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
  const token = getAuthToken()
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
  } catch {
    throw new Error(`Cannot reach Bagly API at ${apiBase}. Is Bagly.Api running on IIS port 8081?`)
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

export const api = {
  health: () => request('/health'),

  login: (email, password) =>
    request('/auth/login', {
      method: 'POST',
      body: { email: String(email).trim(), password: String(password) },
    }),

  logout: () => request('/auth/logout', { method: 'POST', auth: true }),

  me: () => request('/auth/me', { auth: true }),

  getProducts: ({ category, sort } = {}) => {
    const query = new URLSearchParams()
    if (category && category !== 'all') query.set('category', category)
    if (sort && sort !== 'featured') query.set('sort', sort)
    const qs = query.toString()
    return request(`/products${qs ? `?${qs}` : ''}`)
  },

  getProduct: (id) => request(`/products/${encodeURIComponent(id)}`),

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

  createOrder: (payload) => request('/orders', { method: 'POST', body: payload }),

  getOrder: (id) => request(`/orders/${encodeURIComponent(id)}`),

  getOrders: () => request('/orders'),

  getRazorpayConfig: () => request('/payments/razorpay/config'),

  initiateRazorpayPayment: (payload) =>
    request('/payments/razorpay/initiate', { method: 'POST', body: payload }),

  verifyRazorpayPayment: (payload) =>
    request('/payments/razorpay/verify', { method: 'POST', body: payload }),

  reportRazorpayFailure: (payload) =>
    request('/payments/razorpay/failure', { method: 'POST', body: payload }),

  adminGetProducts: () => request('/admin/products', { auth: true }),
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

  adminGetCategories: () => request('/admin/categories', { auth: true }),
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
}
