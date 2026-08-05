import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react'
import { api } from '../api/client'
import { CART_ADD_EVENT, CUSTOMER_LOGOUT_EVENT } from '../constants/events'
import { mapCartFromApi } from '../utils/format'

const CartContext = createContext(null)
const CART_ID_KEY = 'bagly-cart-id'

// Mirrors backend/Bagly.Api/Services/Pricing.cs so the optimistic total shown
// before the API responds matches what the server will confirm.
const FREE_SHIPPING_THRESHOLD = 12999
const STANDARD_SHIPPING = 199

const emptyCart = {
  cartId: null,
  items: [],
  itemCount: 0,
  subtotal: 0,
  shipping: 0,
  total: 0,
}

function calculateShipping(subtotal) {
  return subtotal <= 0 || subtotal >= FREE_SHIPPING_THRESHOLD ? 0 : STANDARD_SHIPPING
}

/** Merges a product add into the current cart client-side, without waiting on the API. */
function buildOptimisticCart(previous, cartId, product, color, quantity) {
  const items = previous.items.map((item) => ({ ...item }))
  const existing = items.find((item) => item.id === product.id && item.color === color)
  if (existing) {
    existing.quantity += quantity
  } else {
    items.push({
      id: product.id,
      name: product.name,
      image: product.image,
      color,
      price: product.price,
      quantity,
    })
  }

  const subtotal = items.reduce((sum, item) => sum + item.price * item.quantity, 0)
  const shipping = calculateShipping(subtotal)

  return {
    cartId,
    items,
    itemCount: items.reduce((sum, item) => sum + item.quantity, 0),
    subtotal,
    shipping,
    total: subtotal + shipping,
  }
}

/** crypto.randomUUID() needs a secure context, which plain-HTTP LAN/IIS access won't have. */
function generateCartId() {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    try {
      return crypto.randomUUID()
    } catch {
      // fall through to the manual fallback below
    }
  }
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0
    const v = c === 'x' ? r : (r & 0x3) | 0x8
    return v.toString(16)
  })
}

export function CartProvider({ children }) {
  const [cart, setCart] = useState(emptyCart)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [loadFailed, setLoadFailed] = useState(false)

  const applyCart = useCallback((apiCart) => {
    const mapped = mapCartFromApi(apiCart)
    setCart(mapped)
    if (mapped.cartId) {
      localStorage.setItem(CART_ID_KEY, mapped.cartId)
    }
    return mapped
  }, [])

  const bootstrapCart = useCallback(async () => {
    setLoading(true)
    setError('')
    setLoadFailed(false)
    try {
      const storedId = localStorage.getItem(CART_ID_KEY)
      if (storedId) {
        try {
          const existing = await api.getCart(storedId)
          applyCart(existing)
          return
        } catch {
          localStorage.removeItem(CART_ID_KEY)
        }
      }

      const created = await api.createCart()
      applyCart(created)
    } catch (err) {
      setError(err.message || 'Unable to load cart from API.')
      setCart(emptyCart)
      setLoadFailed(true)
    } finally {
      setLoading(false)
    }
  }, [applyCart])

  useEffect(() => {
    bootstrapCart()
  }, [bootstrapCart])

  const addItem = useCallback(
    async (product, { color, quantity = 1, sourceRect = null } = {}) => {
      setBusy(true)
      setError('')

      const resolvedColor = color || product.colors?.[0] || 'Default'
      const previousCart = cart
      const isNewCartId = !cart.cartId
      // A cart id is generated client-side (rather than awaiting a create-cart round trip
      // first) so the very first add-to-cart is a single request; the backend creates the
      // row on demand if it doesn't exist yet.
      const cartId = cart.cartId || localStorage.getItem(CART_ID_KEY) || generateCartId()

      // Update the cart badge/totals and fire the fly-to-cart animation immediately —
      // neither should wait on the network. The API call below confirms stock and
      // reconciles pricing in the background, rolling back on failure.
      setCart(buildOptimisticCart(cart, cartId, product, resolvedColor, quantity))
      localStorage.setItem(CART_ID_KEY, cartId)
      window.dispatchEvent(
        new CustomEvent(CART_ADD_EVENT, {
          detail: { sourceRect },
        }),
      )

      try {
        const updated = await api.addCartItem(cartId, {
          productId: product.id,
          color: resolvedColor,
          quantity,
        })
        applyCart(updated)
      } catch (err) {
        setCart(previousCart)
        if (isNewCartId) localStorage.removeItem(CART_ID_KEY)
        setError(err.message || 'Unable to add item.')
        throw err
      } finally {
        setBusy(false)
      }
    },
    [applyCart, cart],
  )

  const removeItem = useCallback(
    async (productId, color) => {
      if (!cart.cartId) return
      setBusy(true)
      setError('')
      try {
        const updated = await api.removeCartItem(cart.cartId, productId, color)
        applyCart(updated)
      } catch (err) {
        setError(err.message || 'Unable to remove item.')
        throw err
      } finally {
        setBusy(false)
      }
    },
    [applyCart, cart.cartId],
  )

  const updateQuantity = useCallback(
    async (productId, color, quantity) => {
      if (!cart.cartId) return
      setBusy(true)
      setError('')
      try {
        const updated = await api.updateCartItem(cart.cartId, productId, color, quantity)
        applyCart(updated)
      } catch (err) {
        setError(err.message || 'Unable to update quantity.')
        throw err
      } finally {
        setBusy(false)
      }
    },
    [applyCart, cart.cartId],
  )

  const clearCart = useCallback(async ({ forget = false } = {}) => {
    setBusy(true)
    setError('')
    try {
      if (cart.cartId) {
        try {
          const updated = await api.clearCart(cart.cartId)
          if (forget) {
            localStorage.removeItem(CART_ID_KEY)
            setCart(emptyCart)
          } else {
            applyCart(updated)
          }
        } catch (err) {
          if (forget) {
            localStorage.removeItem(CART_ID_KEY)
            setCart(emptyCart)
          } else {
            throw err
          }
        }
      } else {
        if (forget) localStorage.removeItem(CART_ID_KEY)
        setCart(emptyCart)
      }
    } catch (err) {
      setError(err.message || 'Unable to clear cart.')
      throw err
    } finally {
      setBusy(false)
    }
  }, [applyCart, cart.cartId])

  const refreshCart = useCallback(async () => {
    if (!cart.cartId) return
    const updated = await api.getCart(cart.cartId)
    applyCart(updated)
  }, [applyCart, cart.cartId])

  useEffect(() => {
    const onCustomerLogout = () => {
      clearCart({ forget: true }).catch(() => {})
    }

    window.addEventListener(CUSTOMER_LOGOUT_EVENT, onCustomerLogout)
    return () => window.removeEventListener(CUSTOMER_LOGOUT_EVENT, onCustomerLogout)
  }, [clearCart])

  const value = useMemo(
    () => ({
      cartId: cart.cartId,
      items: cart.items,
      itemCount: cart.itemCount,
      subtotal: cart.subtotal,
      shipping: cart.shipping,
      total: cart.total,
      loading,
      busy,
      error,
      loadFailed,
      addItem,
      removeItem,
      updateQuantity,
      clearCart,
      refreshCart,
      retryBootstrap: bootstrapCart,
    }),
    [
      cart,
      loading,
      busy,
      error,
      loadFailed,
      addItem,
      removeItem,
      updateQuantity,
      clearCart,
      refreshCart,
      bootstrapCart,
    ],
  )

  return <CartContext.Provider value={value}>{children}</CartContext.Provider>
}

export function useCart() {
  const ctx = useContext(CartContext)
  if (!ctx) throw new Error('useCart must be used within CartProvider')
  return ctx
}
