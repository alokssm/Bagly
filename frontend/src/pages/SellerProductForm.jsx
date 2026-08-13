import { useEffect, useState } from 'react'
import { Link, Navigate, useNavigate, useParams } from 'react-router-dom'
import { api } from '../api/client'
import { useSellerAuth } from '../context/SellerAuthContext'
import { buildUpsertProductPayload } from '../utils/payloads'
import SellerHubNav from '../components/SellerHubNav'

const emptyForm = {
  id: '',
  name: '',
  category: '',
  subCategoryId: '',
  price: '',
  stockQuantity: '999',
  isActive: true,
  image: '',
  shortDescription: '',
  compareAt: '',
  material: '',
  badge: '',
  rating: '4.5',
  reviews: '0',
  colors: '',
  description: '',
  features: '',
  gallery: '',
  slug: '',
  seoTitle: '',
  seoDescription: '',
  seoKeywords: '',
  shiprocketPickupLocation: '',
}

function toForm(product) {
  return {
    id: product.id || '',
    name: product.name || '',
    category: product.category || '',
    subCategoryId: product.subCategoryId || '',
    price: String(product.price ?? ''),
    stockQuantity: String(product.stockQuantity ?? 999),
    isActive: product.isActive !== false,
    image: product.image || '',
    shortDescription: product.shortDescription || '',
    compareAt: product.compareAt != null ? String(product.compareAt) : '',
    material: product.material || '',
    badge: product.badge || '',
    rating: String(product.rating ?? 4.5),
    reviews: String(product.reviews ?? 0),
    colors: (product.colors || []).join(', '),
    description: product.description || '',
    features: (product.features || []).join('\n'),
    gallery: (product.gallery || []).join('\n'),
    slug: product.slug || '',
    seoTitle: product.seoTitle || '',
    seoDescription: product.seoDescription || '',
    seoKeywords: product.seoKeywords || '',
    shiprocketPickupLocation: product.shiprocketPickupLocation || '',
  }
}

export default function SellerProductForm() {
  const { id } = useParams()
  const isEdit = Boolean(id)
  const navigate = useNavigate()
  const { user, logout } = useSellerAuth()
  const approved = (user?.status || '').toLowerCase() === 'approved'

  const [form, setForm] = useState(emptyForm)
  const [categories, setCategories] = useState([])
  const [pickupChoices, setPickupChoices] = useState(['home', 'work'])
  const [pickupCustom, setPickupCustom] = useState(false)
  const [loading, setLoading] = useState(isEdit && approved)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [uploadError, setUploadError] = useState('')
  const [uploading, setUploading] = useState({ image: false, gallery: false })

  useEffect(() => {
    if (!approved) return undefined
    let cancelled = false

    async function load() {
      try {
        const [cats, pickupRes] = await Promise.all([
          api.getCategories(),
          api.sellerGetShiprocketPickupLocations().catch(() => ({ locations: ['home', 'work'] })),
        ])
        if (cancelled) return
        const usable = (cats || []).filter((c) => c.id !== 'all')
        setCategories(usable)
        const locations = (pickupRes?.locations || []).filter(Boolean)
        const choices = locations.length ? locations : ['home', 'work']
        setPickupChoices(choices)

        if (isEdit) {
          const product = await api.sellerGetProduct(id)
          if (!cancelled) {
            const next = toForm(product)
            setForm(next)
            if (next.shiprocketPickupLocation && !choices.includes(next.shiprocketPickupLocation)) {
              setPickupCustom(true)
            }
          }
        } else {
          const defaultCategory = usable.find((c) => !c.parentId)
          if (defaultCategory && !cancelled) {
            setForm((prev) => ({ ...prev, category: defaultCategory.id }))
          }
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
  }, [id, isEdit, approved])

  if (!approved) {
    return <Navigate to="/seller/products" replace />
  }

  const onChange = (e) => {
    const { name, value, type, checked } = e.target
    setForm((prev) => ({
      ...prev,
      [name]: type === 'checkbox' ? checked : value,
      ...(name === 'category' ? { subCategoryId: '' } : {}),
    }))
  }

  const subCategoryOptions = categories.filter((cat) => cat.parentId === form.category)

  const handleImageUpload = async (e, target) => {
    const file = e.target.files?.[0]
    e.target.value = ''
    if (!file) return

    setUploadError('')
    setUploading((prev) => ({ ...prev, [target]: true }))

    try {
      const { url } = await api.sellerUploadImage(file)
      if (target === 'image') {
        setForm((prev) => ({ ...prev, image: url }))
      } else {
        setForm((prev) => ({
          ...prev,
          gallery: prev.gallery ? `${prev.gallery}\n${url}` : url,
        }))
      }
    } catch (err) {
      setUploadError(err.message || 'Image upload failed.')
    } finally {
      setUploading((prev) => ({ ...prev, [target]: false }))
    }
  }

  const onSubmit = async (e) => {
    e.preventDefault()
    setSaving(true)
    setError('')

    const payload = buildUpsertProductPayload(form, { includeId: !isEdit })

    try {
      if (isEdit) await api.sellerUpdateProduct(id, payload)
      else await api.sellerCreateProduct(payload)
      navigate('/seller/products')
    } catch (err) {
      setError(err.message || 'Save failed.')
    } finally {
      setSaving(false)
    }
  }

  if (loading) {
    return (
      <div className="seller-page">
        <div className="seller-shell">
          <p className="admin-muted">Loading…</p>
        </div>
      </div>
    )
  }

  const galleryPreviewUrls = form.gallery
    .split('\n')
    .map((url) => url.trim())
    .filter(Boolean)

  return (
    <div className="seller-page">
      <div className="seller-shell seller-shell--wide">
        <header className="seller-head">
          <div>
            <p className="eyebrow">Seller hub</p>
            <h1>{isEdit ? 'Edit product' : 'Add product'}</h1>
            <p className="seller-lead">Active products appear in the Bagly shop when in stock.</p>
          </div>
          <button type="button" className="btn btn-ghost" onClick={logout}>
            Sign out
          </button>
        </header>

        <SellerHubNav />

        {error ? <p className="admin-error">{error}</p> : null}
        {uploadError ? <p className="admin-error">{uploadError}</p> : null}

        <form className="seller-form pf-form" onSubmit={onSubmit}>
          <section className="pf-section">
            <p className="pf-section-title">Basics</p>
            <div className="pf-grid">
              <div className="form-field">
                <label htmlFor="name">Name</label>
                <input id="name" name="name" required value={form.name} onChange={onChange} />
              </div>

              <div className="form-field">
                <label htmlFor="category">Category</label>
                <select id="category" name="category" required value={form.category} onChange={onChange}>
                  <option value="">Select category</option>
                  {categories
                    .filter((cat) => !cat.parentId)
                    .map((cat) => (
                      <option key={cat.id} value={cat.id}>
                        {cat.label}
                      </option>
                    ))}
                </select>
              </div>

              {subCategoryOptions.length ? (
                <div className="form-field">
                  <label htmlFor="subCategoryId">Subcategory</label>
                  <select
                    id="subCategoryId"
                    name="subCategoryId"
                    value={form.subCategoryId}
                    onChange={onChange}
                  >
                    <option value="">None</option>
                    {subCategoryOptions.map((cat) => (
                      <option key={cat.id} value={cat.id}>
                        {cat.label}
                      </option>
                    ))}
                  </select>
                </div>
              ) : null}

              <div className="form-field">
                <label htmlFor="price">Price (₹)</label>
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
              </div>

              <label className="admin-check">
                <input type="checkbox" name="isActive" checked={form.isActive} onChange={onChange} />
                Active (visible in store)
              </label>

              <div className="form-field">
                <label htmlFor="shiprocketPickupLocation">Shiprocket pickup</label>
                {pickupCustom ? (
                  <input
                    id="shiprocketPickupLocation"
                    name="shiprocketPickupLocation"
                    value={form.shiprocketPickupLocation}
                    onChange={onChange}
                    placeholder="Exact Shiprocket nickname"
                  />
                ) : (
                  <select
                    id="shiprocketPickupLocation"
                    name="shiprocketPickupLocation"
                    value={form.shiprocketPickupLocation}
                    onChange={(e) => {
                      if (e.target.value === '__custom__') {
                        setPickupCustom(true)
                        setForm((prev) => ({ ...prev, shiprocketPickupLocation: '' }))
                        return
                      }
                      onChange(e)
                    }}
                  >
                    <option value="">Platform default</option>
                    {pickupChoices.map((nick) => (
                      <option key={nick} value={nick}>
                        {nick}
                      </option>
                    ))}
                    <option value="__custom__">Other nickname…</option>
                  </select>
                )}
                <small>
                  Exact nickname from Shiprocket → Settings → Pickup Addresses (e.g. home, work). Empty uses the
                  platform default.
                </small>
                {pickupCustom ? (
                  <button
                    type="button"
                    className="btn btn-ghost btn-sm"
                    style={{ marginTop: '0.35rem' }}
                    onClick={() => {
                      setPickupCustom(false)
                      setForm((prev) => ({ ...prev, shiprocketPickupLocation: '' }))
                    }}
                  >
                    Use list instead
                  </button>
                ) : null}
              </div>

              <div className="form-field full">
                <label htmlFor="image">Image URL</label>
                <input id="image" name="image" required value={form.image} onChange={onChange} />
                <div className="admin-image-upload">
                  {form.image ? (
                    <img src={form.image} alt="Current product" className="admin-image-preview" />
                  ) : null}
                  <label className="btn btn-secondary">
                    {uploading.image ? 'Uploading…' : 'Upload image'}
                    <input
                      type="file"
                      accept="image/jpeg,image/png,image/webp,image/gif"
                      className="file-input-hidden"
                      disabled={uploading.image}
                      onChange={(e) => handleImageUpload(e, 'image')}
                    />
                  </label>
                  <small>Uploads go to Cloudinary (JPEG/PNG/WEBP/GIF, max 5&nbsp;MB).</small>
                </div>
              </div>

              <div className="form-field full">
                <label htmlFor="shortDescription">Short description</label>
                <input
                  id="shortDescription"
                  name="shortDescription"
                  value={form.shortDescription}
                  onChange={onChange}
                  placeholder="One line shown on product cards"
                />
              </div>

              <div className="form-field full">
                <label htmlFor="description">Description</label>
                <textarea
                  id="description"
                  name="description"
                  rows={3}
                  value={form.description}
                  onChange={onChange}
                />
              </div>
            </div>
          </section>

          <details className="pf-advanced">
            <summary>More details (gallery, colors, SEO)</summary>
            <div className="pf-advanced-body">
              <div className="pf-grid">
                <div className="form-field">
                  <label htmlFor="compareAt">Compare-at price (₹)</label>
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
                  <input
                    id="badge"
                    name="badge"
                    value={form.badge}
                    onChange={onChange}
                    placeholder="New, Bestseller…"
                  />
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
                  <label htmlFor="gallery">Gallery URLs (one per line)</label>
                  <textarea id="gallery" name="gallery" rows={3} value={form.gallery} onChange={onChange} />
                  <div className="admin-image-upload">
                    <label className="btn btn-secondary">
                      {uploading.gallery ? 'Uploading…' : 'Add gallery image'}
                      <input
                        type="file"
                        accept="image/jpeg,image/png,image/webp,image/gif"
                        className="file-input-hidden"
                        disabled={uploading.gallery}
                        onChange={(e) => handleImageUpload(e, 'gallery')}
                      />
                    </label>
                  </div>
                  {galleryPreviewUrls.length ? (
                    <div className="admin-gallery-preview">
                      {galleryPreviewUrls.map((url, index) => (
                        <img key={`${url}-${index}`} src={url} alt="" />
                      ))}
                    </div>
                  ) : null}
                </div>
                <div className="form-field full">
                  <label htmlFor="features">Features (one per line)</label>
                  <textarea id="features" name="features" rows={3} value={form.features} onChange={onChange} />
                </div>
                <div className="form-field full">
                  <label htmlFor="slug">URL slug</label>
                  <input
                    id="slug"
                    name="slug"
                    value={form.slug}
                    onChange={onChange}
                    placeholder="auto-generated-from-name"
                  />
                </div>
                <div className="form-field full">
                  <label htmlFor="seoTitle">Meta title</label>
                  <input id="seoTitle" name="seoTitle" value={form.seoTitle} onChange={onChange} maxLength={70} />
                </div>
                <div className="form-field full">
                  <label htmlFor="seoDescription">Meta description</label>
                  <textarea
                    id="seoDescription"
                    name="seoDescription"
                    rows={2}
                    maxLength={200}
                    value={form.seoDescription}
                    onChange={onChange}
                  />
                </div>
              </div>
            </div>
          </details>

          <div className="seller-form-actions">
            <button type="submit" className="btn btn-primary" disabled={saving}>
              {saving ? 'Saving…' : isEdit ? 'Save changes' : 'Create product'}
            </button>
            <Link to="/seller/products" className="btn btn-ghost">
              Cancel
            </Link>
          </div>
        </form>
      </div>
    </div>
  )
}
