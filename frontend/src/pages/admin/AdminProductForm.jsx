import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { api } from '../../api/client'
import { buildUpsertProductPayload } from '../../utils/payloads'

const emptyForm = {
  id: '',
  name: '',
  category: '',
  price: '',
  compareAt: '',
  colors: '',
  material: '',
  rating: '4.5',
  reviews: '0',
  badge: '',
  shortDescription: '',
  description: '',
  features: '',
  image: '',
  gallery: '',
  isActive: true,
  stockQuantity: '999',
}

function toForm(product) {
  return {
    id: product.id || '',
    name: product.name || '',
    category: product.category || '',
    price: String(product.price ?? ''),
    compareAt: product.compareAt != null ? String(product.compareAt) : '',
    colors: (product.colors || []).join(', '),
    material: product.material || '',
    rating: String(product.rating ?? 4.5),
    reviews: String(product.reviews ?? 0),
    badge: product.badge || '',
    shortDescription: product.shortDescription || '',
    description: product.description || '',
    features: (product.features || []).join('\n'),
    image: product.image || '',
    gallery: (product.gallery || []).join('\n'),
    isActive: product.isActive !== false,
    stockQuantity: String(product.stockQuantity ?? 999),
  }
}

export default function AdminProductForm() {
  const { id } = useParams()
  const isEdit = Boolean(id)
  const navigate = useNavigate()
  const [form, setForm] = useState(emptyForm)
  const [categories, setCategories] = useState([])
  const [loading, setLoading] = useState(isEdit)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    let cancelled = false

    async function load() {
      try {
        const cats = await api.adminGetCategories()
        if (cancelled) return
        const usable = cats.filter((c) => c.id !== 'all')
        setCategories(usable)

        if (isEdit) {
          const product = await api.adminGetProduct(id)
          if (!cancelled) setForm(toForm(product))
        } else if (usable[0] && !cancelled) {
          setForm((prev) => ({ ...prev, category: usable[0].id }))
        }
      } catch (err) {
        if (!cancelled) setError(err.message || 'Unable to load form data.')
      } finally {
        if (!cancelled) setLoading(false)
      }
    }

    load()
    return () => {
      cancelled = true
    }
  }, [id, isEdit])

  const onChange = (e) => {
    const { name, value, type, checked } = e.target
    setForm((prev) => ({ ...prev, [name]: type === 'checkbox' ? checked : value }))
  }

  const onSubmit = async (e) => {
    e.preventDefault()
    setSaving(true)
    setError('')

    const payload = buildUpsertProductPayload(form, { includeId: !isEdit })

    try {
      if (isEdit) await api.adminUpdateProduct(id, payload)
      else await api.adminCreateProduct(payload)
      navigate('/admin/products')
    } catch (err) {
      setError(err.message || 'Save failed.')
    } finally {
      setSaving(false)
    }
  }

  if (loading) {
    return (
      <div className="admin-page">
        <p>Loading…</p>
      </div>
    )
  }

  return (
    <div className="admin-page">
      <div className="admin-page-head">
        <div>
          <p className="eyebrow">Catalog</p>
          <h1>{isEdit ? 'Edit product' : 'Add product'}</h1>
        </div>
        <Link to="/admin/products" className="btn btn-secondary">
          Back
        </Link>
      </div>

      {error ? <p className="admin-error">{error}</p> : null}

      <form className="admin-form" onSubmit={onSubmit}>
        <div className="admin-form-grid">
          {!isEdit ? (
            <div className="form-field">
              <label htmlFor="id">Slug / ID (optional)</label>
              <input id="id" name="id" value={form.id} onChange={onChange} placeholder="auto-from-name" />
            </div>
          ) : (
            <div className="form-field">
              <label>ID</label>
              <input value={form.id} disabled />
            </div>
          )}

          <div className="form-field">
            <label htmlFor="name">Name</label>
            <input id="name" name="name" required value={form.name} onChange={onChange} />
          </div>

          <div className="form-field">
            <label htmlFor="category">Category</label>
            <select id="category" name="category" required value={form.category} onChange={onChange}>
              <option value="">Select category</option>
              {categories.map((cat) => (
                <option key={cat.id} value={cat.id}>
                  {cat.label}
                </option>
              ))}
            </select>
          </div>

          <div className="form-field">
            <label htmlFor="price">Price</label>
            <input
              id="price"
              name="price"
              type="number"
              min="0"
              step="0.01"
              required
              value={form.price}
              onChange={onChange}
            />
          </div>

          <div className="form-field">
            <label htmlFor="compareAt">Compare-at price</label>
            <input
              id="compareAt"
              name="compareAt"
              type="number"
              min="0"
              step="0.01"
              value={form.compareAt}
              onChange={onChange}
            />
          </div>

          <div className="form-field">
            <label htmlFor="material">Material</label>
            <input id="material" name="material" value={form.material} onChange={onChange} />
          </div>

          <div className="form-field">
            <label htmlFor="badge">Badge</label>
            <input id="badge" name="badge" value={form.badge} onChange={onChange} placeholder="New, Bestseller…" />
          </div>

          <div className="form-field">
            <label htmlFor="rating">Rating</label>
            <input
              id="rating"
              name="rating"
              type="number"
              min="0"
              max="5"
              step="0.1"
              value={form.rating}
              onChange={onChange}
            />
          </div>

          <div className="form-field">
            <label htmlFor="reviews">Reviews</label>
            <input
              id="reviews"
              name="reviews"
              type="number"
              min="0"
              value={form.reviews}
              onChange={onChange}
            />
          </div>

          <div className="form-field">
            <label htmlFor="stockQuantity">Stock quantity</label>
            <input
              id="stockQuantity"
              name="stockQuantity"
              type="number"
              min="0"
              step="1"
              value={form.stockQuantity}
              onChange={onChange}
            />
            <small>0 means out of stock — the storefront chat will offer restock alerts.</small>
          </div>

          <div className="form-field full">
            <label htmlFor="colors">Colors (comma-separated)</label>
            <input
              id="colors"
              name="colors"
              value={form.colors}
              onChange={onChange}
              placeholder="Black, Camel, Olive"
            />
          </div>

          <div className="form-field full">
            <label htmlFor="image">Image URL</label>
            <input id="image" name="image" required value={form.image} onChange={onChange} />
          </div>

          <div className="form-field full">
            <label htmlFor="gallery">Gallery URLs (one per line)</label>
            <textarea id="gallery" name="gallery" rows={3} value={form.gallery} onChange={onChange} />
          </div>

          <div className="form-field full">
            <label htmlFor="shortDescription">Short description</label>
            <input
              id="shortDescription"
              name="shortDescription"
              value={form.shortDescription}
              onChange={onChange}
            />
          </div>

          <div className="form-field full">
            <label htmlFor="description">Description</label>
            <textarea
              id="description"
              name="description"
              rows={4}
              value={form.description}
              onChange={onChange}
            />
          </div>

          <div className="form-field full">
            <label htmlFor="features">Features (one per line)</label>
            <textarea id="features" name="features" rows={4} value={form.features} onChange={onChange} />
          </div>

          <label className="admin-check full">
            <input type="checkbox" name="isActive" checked={form.isActive} onChange={onChange} />
            Active (visible in store)
          </label>
        </div>

        <div className="admin-actions-row">
          <button type="submit" className="btn btn-primary" disabled={saving}>
            {saving ? 'Saving…' : isEdit ? 'Save changes' : 'Create product'}
          </button>
          <Link to="/admin/products" className="btn btn-secondary">
            Cancel
          </Link>
        </div>
      </form>
    </div>
  )
}
