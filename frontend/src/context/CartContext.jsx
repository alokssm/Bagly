import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react'
import { api } from '../api/client'
import { CUSTOMER_LOGOUT_EVENT } from '../constants/events'
import { mapCartFromApi } from '../utils/format'

const CartContext = createContext(null)
const CART_ID_KEY = 'bagly-cart-id'

const emptyCart = {
  cartId: null,
  items: [],
  itemCount: 0,
  subtotal: 0,
  shipping: 0,
  total: 0,
}

export function CartProvider({ children }) {
  const [cart, setCart] = useState(emptyCart)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const applyCart = useCallback((apiCart) => {
    const mapped = mapCartFromApi(apiCart)
    setCart(mapped)
    if (mapped.cartId) {
      localStorage.setItem(CART_ID_KEY, mapped.cartId)
    }
    return mapped
  }, [])

  const ensureCartId = useCallback(async () => {
    if (cart.cartId) return cart.cartId

    const storedId = localStorage.getItem(CART_ID_KEY)
    if (storedId) {
      try {
        const existing = await api.getCart(storedId)
        applyCart(existing)
        return existing.cartId
      } catch {
        localStorage.removeItem(CART_ID_KEY)
      }
    }

    const created = await api.createCart()
    applyCart(created)
    return created.cartId
  }, [applyCart, cart.cartId])

  useEffect(() => {
    let cancelled = false

    async function bootstrap() {
      setLoading(true)
      setError('')
      try {
        const storedId = localStorage.getItem(CART_ID_KEY)
        if (storedId) {
          try {
            const existing = await api.getCart(storedId)
            if (!cancelled) applyCart(existing)
            return
          } catch {
            localStorage.removeItem(CART_ID_KEY)
          }
        }

        const created = await api.createCart()
        if (!cancelled) applyCart(created)
      } catch (err) {
        if (!cancelled) {
          setError(err.message || 'Unable to load cart from API.')
          setCart(emptyCart)
        }
      } finally {
        if (!cancelled) setLoading(false)
      }
    }

    bootstrap()
    return () => {
      cancelled = true
    }
  }, [applyCart])

  const addItem = useCallback(
    async (product, { color, quantity = 1 } = {}) => {
      setBusy(true)
      setError('')
      try {
        const cartId = await ensureCartId()
        const updated = await api.addCartItem(cartId, {
          productId: product.id,
          color: color || product.colors?.[0] || 'Default',
          quantity,
        })
        applyCart(updated)
      } catch (err) {
        setError(err.message || 'Unable to add item.')
        throw err
      } finally {
        setBusy(false)
      }
    },
    [applyCart, ensureCartId],
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
      addItem,
      removeItem,
      updateQuantity,
      clearCart,
      refreshCart,
    }),
    [
      cart,
      loading,
      busy,
      error,
      addItem,
      removeItem,
      updateQuantity,
      clearCart,
      refreshCart,
    ],
  )

  return <CartContext.Provider value={value}>{children}</CartContext.Provider>
}

export function useCart() {
  const ctx = useContext(CartContext)
  if (!ctx) throw new Error('useCart must be used within CartProvider')
  return ctx
}
