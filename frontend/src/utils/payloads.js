/** Builders that match Bagly.Api DTO contracts (camelCase JSON). */

export function buildLoginPayload(email, password) {
  return {
    email: String(email || '').trim(),
    password: String(password || ''),
  }
}

export function buildAddCartItemPayload({ productId, color, quantity = 1 }) {
  return {
    productId: String(productId),
    color: color ? String(color) : null,
    quantity: Math.max(1, Number(quantity) || 1),
  }
}

export function buildUpdateCartItemPayload(quantity) {
  return {
    quantity: Number(quantity) || 0,
  }
}

export function buildCreateOrderPayload({
  email,
  firstName,
  lastName,
  address,
  city,
  state,
  zip,
  country,
  cartId,
  items,
}) {
  const payload = {
    email: String(email || '').trim(),
    firstName: String(firstName || '').trim(),
    lastName: String(lastName || '').trim(),
    address: String(address || '').trim(),
    city: String(city || '').trim(),
    state: String(state || '').trim(),
    zip: String(zip || '').trim(),
    country: String(country || 'United States').trim(),
  }

  if (cartId) {
    payload.cartId = String(cartId)
  } else if (Array.isArray(items) && items.length > 0) {
    payload.items = items.map((item) => ({
      productId: String(item.productId || item.id),
      color: String(item.color || 'Default'),
      quantity: Math.max(1, Number(item.quantity) || 1),
    }))
  }

  return payload
}

export function buildShippingAddressPayload({
  label,
  firstName,
  lastName,
  email,
  phone,
  address,
  city,
  state,
  zip,
  country,
  isDefault = false,
}) {
  return {
    label: label ? String(label).trim() : null,
    firstName: String(firstName || '').trim(),
    lastName: String(lastName || '').trim(),
    email: String(email || '').trim(),
    phone: phone ? String(phone).trim() : null,
    address: String(address || '').trim(),
    city: String(city || '').trim(),
    state: String(state || '').trim(),
    zip: String(zip || '').trim(),
    country: String(country || 'India').trim(),
    isDefault: Boolean(isDefault),
  }
}

export function buildContactPayload({ firstName, lastName, phone, email, companyName, message }) {
  return {
    firstName: String(firstName || '').trim(),
    lastName: String(lastName || '').trim(),
    phone: String(phone || '').trim(),
    email: String(email || '').trim(),
    companyName: companyName ? String(companyName).trim() : null,
    message: String(message || '').trim(),
  }
}

function splitList(value, { multiline = false } = {}) {
  if (!value?.trim()) return []
  if (multiline) {
    return value
      .split('\n')
      .map((x) => x.trim())
      .filter(Boolean)
  }
  return value
    .split(',')
    .map((x) => x.trim())
    .filter(Boolean)
}

export function buildUpsertProductPayload(form, { includeId = false } = {}) {
  const image = String(form.image || '').trim()
  const colors = splitList(form.colors)
  const features = splitList(form.features, { multiline: true })
  let gallery = splitList(form.gallery, { multiline: true })
  if (gallery.length === 0 && image) gallery = [image]

  const payload = {
    name: String(form.name || '').trim(),
    category: String(form.category || '').trim(),
    subCategoryId: String(form.subCategoryId || '').trim() || null,
    price: Number(form.price),
    compareAt: form.compareAt === '' || form.compareAt == null ? null : Number(form.compareAt),
    colors: colors.length ? colors : ['Default'],
    material: String(form.material || '').trim(),
    rating: Number(form.rating) || 0,
    reviews: Number(form.reviews) || 0,
    badge: String(form.badge || '').trim() || null,
    shortDescription: String(form.shortDescription || '').trim(),
    description: String(form.description || '').trim(),
    features,
    image,
    gallery,
    isActive: Boolean(form.isActive),
    stockQuantity: Math.max(0, Number(form.stockQuantity) || 0),
  }

  if (includeId && form.id) {
    payload.id = String(form.id).trim()
  }

  return payload
}

export function buildUpsertCategoryPayload(form) {
  return {
    id: String(form.id || '').trim(),
    label: String(form.label || '').trim(),
    sortOrder: Number(form.sortOrder) || 0,
    isActive: Boolean(form.isActive),
    parentId: String(form.parentId || '').trim() || null,
  }
}
