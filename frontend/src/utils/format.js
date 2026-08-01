/** Bagly prices are Indian Rupees only — always formatted as ₹ / en-IN. */
export function formatPrice(value) {
  return new Intl.NumberFormat('en-IN', {
    style: 'currency',
    currency: 'INR',
    maximumFractionDigits: 0,
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
