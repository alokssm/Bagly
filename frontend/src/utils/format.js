/** Bagly prices are Indian Rupees only — always formatted as ₹ / en-IN. */
export function formatPrice(value, options = {}) {
  const fractionDigits =
    typeof options === 'object' && options != null && Number.isFinite(options.fractionDigits)
      ? options.fractionDigits
      : 0

  return new Intl.NumberFormat('en-IN', {
    style: 'currency',
    currency: 'INR',
    minimumFractionDigits: fractionDigits,
    maximumFractionDigits: fractionDigits,
  }).format(Number(value) || 0)
}

/** Shipping / courier money — keep paise (e.g. ₹242.36). */
export function formatShippingPrice(value) {
  return formatPrice(value, { fractionDigits: 2 })
}

export function mapCartFromApi(cart) {
  if (!cart) {
    return {
      cartId: null,
      items: [],
      itemCount: 0,
      subtotal: 0,
      shipping: 0,
      total: 0,
    }
  }

  return {
    cartId: cart.cartId,
    items: (cart.items || []).map((item) => ({
      id: item.productId,
      name: item.name,
      image: item.image,
      color: item.color,
      price: item.price,
      quantity: item.quantity,
    })),
    itemCount: cart.itemCount ?? 0,
    subtotal: cart.subtotal ?? 0,
    shipping: cart.shipping ?? 0,
    total: cart.total ?? 0,
  }
}
