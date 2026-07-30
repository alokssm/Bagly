export function formatPrice(value, currency = 'USD') {
  const code = currency === 'INR' ? 'INR' : 'USD'
  const locale = code === 'INR' ? 'en-IN' : 'en-US'
  return new Intl.NumberFormat(locale, {
    style: 'currency',
    currency: code,
    maximumFractionDigits: code === 'INR' ? 2 : 0,
  }).format(Number(value) || 0)
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
