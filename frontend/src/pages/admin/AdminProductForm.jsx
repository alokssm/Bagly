import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { api } from '../../api/client'
import { buildUpsertProductPayload } from '../../utils/payloads'

const emptyForm = {
  // Primary
  id: '',
  name: '',
  category: '',
  subCategoryId: '',
  price: '',
  stockQuantity: '999',
  isActive: true,
  image: '',
  shortDescription: '',
  // Advanced (collapsed by default)
  compareAt: '',
  material: '',
  badge: '',
  rating: '4.5',
  reviews: '0',
  colors: '',
  description: '',
  features: '',
  gallery: '',
  // SEO
  slug: '',
  seoTitle: '',
  seoDescription: '',
  seoKeywords: '',
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
  }
}

function CharCount({ value, max }) {
  const length = (value || '').length
  return (
    <span className={`pf-char-count${length > max ? ' over' : ''}`}>
      {length}/{max} characters
    </span>
  )
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
  const [uploadError, setUploadError] = useState('')
  const [uploading, setUploading] = useState({ image: false, gallery: false })

  useEffect(() => {
    let cancelled = false

    async function load() {
      try {
        // Category counts are small in practice; pull the max page so every category is available
        // for the dropdowns below regardless of the admin categories table's own pagination.
        const catsResult = await api.adminGetCategories({ pageSize: 100 })
        if (cancelled) return
        const usable = (catsResult.items || []).filter((c) => c.id !== 'all')
        setCategories(usable)

        if (isEdit) {
          const product = await api.adminGetProduct(id)
          if (!cancelled) setForm(toForm(product))
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
  }, [id, isEdit])

  const onChange = (e) => {
    const { name, value, type, checked } = e.target
    setForm((prev) => ({
      ...prev,
      [name]: type === 'checkbox' ? checked : value,
      // Switching the top-level category invalidates any previously picked subcategory.
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
      const { url } = await api.adminUploadImage(file)
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

  const galleryPreviewUrls = form.gallery
    .split('\n')
    .map((url) => url.trim())
    .filter(Boolean)

  return (
    <div className="admin-page admin-page-narrow">
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
      {uploadError ? <p className="admin-error">{uploadError}</p> : null}

      <form className="admin-form pf-form" onSubmit={onSubmit}>
        {/* Primary — the essentials needed to list a product. */}
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
                <select id="subCategoryId" name="subCategoryId" value={form.subCategoryId} onChange={onChange}>
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
          </div>
        </section>

        {/* Advanced — everything less commonly edited, tucked away to keep the form short. */}
        <details className="pf-advanced">
          <summary>Advanced details (colors, pricing, gallery, description)</summary>
          <div className="pf-advanced-body">
            {!isEdit ? (
              <div className="form-field">
                <label htmlFor="id">Custom ID (optional)</label>
                <input id="id" name="id" value={form.id} onChange={onChange} placeholder="auto-from-name" />
              </div>
            ) : (
              <div className="form-field">
                <label>ID</label>
                <input value={form.id} disabled />
              </div>
            )}

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
                  <small>Uploaded images are appended as a new line above.</small>
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
                <label htmlFor="description">Full description</label>
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
            </div>
          </div>
        </details>

        {/* SEO — used for the storefront URL, page title and meta description. */}
        <section className="pf-section">
          <p className="pf-section-title">SEO</p>
          <p className="pf-section-hint">
            Controls the product's URL and how it appears in search results and shared links.
          </p>
          <div className="pf-grid">
            <div className="form-field full">
              <label htmlFor="slug">URL slug</label>
              <input
                id="slug"
                name="slug"
                value={form.slug}
                onChange={onChange}
                placeholder="auto-generated-from-name"
              />
              <small>Leave blank to auto-generate from the product name.</small>
            </div>

            <div className="form-field full">
              <label htmlFor="seoTitle">Meta title</label>
              <input
                id="seoTitle"
                name="seoTitle"
                value={form.seoTitle}
                onChange={onChange}
                maxLength={70}
                placeholder={form.name || 'Falls back to product name'}
              />
              <CharCount value={form.seoTitle} max={60} />
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
                placeholder={form.shortDescription || 'Falls back to short description'}
              />
              <CharCount value={form.seoDescription} max={160} />
            </div>

            <div className="form-field full">
              <label htmlFor="seoKeywords">Keywords (comma-separated)</label>
              <input
                id="seoKeywords"
                name="seoKeywords"
                value={form.seoKeywords}
                onChange={onChange}
                placeholder="leather tote, canvas bag, school bag"
              />
            </div>
          </div>
        </section>

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
