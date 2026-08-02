import { useCallback, useEffect, useState } from 'react'
import { api } from '../../api/client'
import { buildUpsertCategoryPayload } from '../../utils/payloads'

const emptyForm = { id: '', label: '', sortOrder: 0, isActive: true, parentId: '' }
const PAGE_SIZE = 50
const emptyResult = { items: [], totalCount: 0, totalPages: 0, page: 1, pageSize: PAGE_SIZE }

export default function AdminCategories() {
  // `allCategories` is an unpaged lookup (kept small since real category counts are low) used to
  // populate the parent-category dropdown, independent of whatever page/search the table below shows.
  const [allCategories, setAllCategories] = useState([])
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [result, setResult] = useState(emptyResult)
  const [form, setForm] = useState(emptyForm)
  const [editingId, setEditingId] = useState(null)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    const handle = setTimeout(() => {
      setSearch(searchInput.trim())
      setPage(1)
    }, 300)
    return () => clearTimeout(handle)
  }, [searchInput])

  const loadAll = useCallback(async () => {
    try {
      const data = await api.adminGetCategories({ pageSize: 100 })
      setAllCategories(data.items || [])
    } catch {
      // The table load below will surface a visible error; the dropdown just stays empty.
    }
  }, [])

  const loadPage = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      const data = await api.adminGetCategories({ page, pageSize: PAGE_SIZE, search: search || undefined })
      setResult({
        items: data.items || [],
        totalCount: data.totalCount || 0,
        totalPages: data.totalPages || 0,
        page: data.page || page,
        pageSize: data.pageSize || PAGE_SIZE,
      })
    } catch (err) {
      setError(err.message || 'Unable to load categories.')
      setResult(emptyResult)
    } finally {
      setLoading(false)
    }
  }, [page, search])

  useEffect(() => {
    loadAll()
  }, [loadAll])

  useEffect(() => {
    loadPage()
  }, [loadPage])

  const resetForm = () => {
    setForm(emptyForm)
    setEditingId(null)
  }

  const startEdit = (category) => {
    setEditingId(category.id)
    setForm({
      id: category.id,
      label: category.label,
      sortOrder: category.sortOrder ?? 0,
      isActive: category.isActive !== false,
      parentId: category.parentId || '',
    })
  }

  const onChange = (e) => {
    const { name, value, type, checked } = e.target
    setForm((prev) => ({
      ...prev,
      [name]: type === 'checkbox' ? checked : name === 'sortOrder' ? Number(value) : value,
    }))
  }

  const topLevelCategories = allCategories.filter((cat) => !cat.parentId && cat.id !== editingId)

  const onSubmit = async (e) => {
    e.preventDefault()
    setSaving(true)
    setError('')

    const payload = buildUpsertCategoryPayload(form)

    try {
      if (editingId) {
        await api.adminUpdateCategory(editingId, payload)
      } else {
        await api.adminCreateCategory(payload)
      }
      resetForm()
      await Promise.all([loadPage(), loadAll()])
    } catch (err) {
      setError(err.message || 'Save failed.')
    } finally {
      setSaving(false)
    }
  }

  const handleDelete = async (id) => {
    if (id === 'all') return
    if (!window.confirm(`Delete category "${id}"?`)) return
    setError('')
    try {
      await api.adminDeleteCategory(id)
      if (editingId === id) resetForm()
      if (result.items.length === 1 && page > 1) {
        setPage((p) => p - 1)
      } else {
        await loadPage()
      }
      await loadAll()
    } catch (err) {
      setError(err.message || 'Delete failed.')
    }
  }

  const { items: categories, totalCount, totalPages } = result
  const from = totalCount === 0 ? 0 : (result.page - 1) * result.pageSize + 1
  const to = Math.min(result.page * result.pageSize, totalCount)

  return (
    <div className="admin-page">
      <div className="admin-page-head">
        <div>
          <p className="eyebrow">Catalog</p>
          <h1>Categories</h1>
        </div>
      </div>

      {error ? <p className="admin-error">{error}</p> : null}

      <div className="admin-split">
        <form className="admin-form" onSubmit={onSubmit}>
          <h2>{editingId ? 'Edit category' : 'Add category'}</h2>
          <div className="admin-form-grid">
            <div className="form-field">
              <label htmlFor="cat-id">ID / slug</label>
              <input
                id="cat-id"
                name="id"
                required
                value={form.id}
                onChange={onChange}
                disabled={Boolean(editingId)}
                placeholder="travel"
              />
            </div>
            <div className="form-field">
              <label htmlFor="cat-label">Label</label>
              <input
                id="cat-label"
                name="label"
                required
                value={form.label}
                onChange={onChange}
                placeholder="Travel"
              />
            </div>
            <div className="form-field">
              <label htmlFor="cat-sort">Sort order</label>
              <input
                id="cat-sort"
                name="sortOrder"
                type="number"
                value={form.sortOrder}
                onChange={onChange}
              />
            </div>
            <div className="form-field">
              <label htmlFor="cat-parent">Parent category (optional)</label>
              <select id="cat-parent" name="parentId" value={form.parentId} onChange={onChange}>
                <option value="">None (top-level category)</option>
                {topLevelCategories
                  .filter((cat) => cat.id !== 'all')
                  .map((cat) => (
                    <option key={cat.id} value={cat.id}>
                      {cat.label}
                    </option>
                  ))}
              </select>
              <small>Use this to add a subcategory, e.g. Boys/Girls/Kids under School Bags.</small>
            </div>
          </div>
          <label className="admin-check full">
            <input type="checkbox" name="isActive" checked={form.isActive} onChange={onChange} />
            Active (visible in storefront filters)
          </label>
          <div className="admin-actions-row">
            <button type="submit" className="btn btn-primary" disabled={saving}>
              {saving ? 'Saving…' : editingId ? 'Save changes' : 'Add category'}
            </button>
            {editingId ? (
              <button type="button" className="btn btn-secondary" onClick={resetForm}>
                Cancel
              </button>
            ) : null}
          </div>
        </form>

        <div>
          <div className="admin-search-bar">
            <label>
              Search
              <input
                type="search"
                value={searchInput}
                onChange={(e) => setSearchInput(e.target.value)}
                placeholder="Label or ID…"
              />
            </label>
          </div>

          <div className="admin-table-wrap">
            {loading ? (
              <p className="admin-muted">Loading…</p>
            ) : categories.length === 0 ? (
              <p className="admin-muted">
                {search ? `No categories match "${search}".` : 'No categories yet.'}
              </p>
            ) : (
              <table className="admin-table">
                <thead>
                  <tr>
                    <th>ID</th>
                    <th>Label</th>
                    <th>Parent</th>
                    <th>Sort</th>
                    <th>Status</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {categories.map((category) => (
                    <tr key={category.id}>
                      <td>{category.id}</td>
                      <td>{category.label}</td>
                      <td>{category.parentLabel || category.parentId || '—'}</td>
                      <td>{category.sortOrder}</td>
                      <td>
                        <span className={`admin-pill ${category.isActive ? 'on' : 'off'}`}>
                          {category.isActive ? 'Active' : 'Hidden'}
                        </span>
                      </td>
                      <td className="admin-row-actions">
                        <button type="button" className="btn btn-secondary" onClick={() => startEdit(category)}>
                          Edit
                        </button>
                        <button
                          type="button"
                          className="btn btn-danger"
                          disabled={category.id === 'all'}
                          onClick={() => handleDelete(category.id)}
                        >
                          Delete
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>

          <div className="admin-pagination">
            <p className="admin-muted">
              {totalCount === 0 ? '0 categories' : `Showing ${from}–${to} of ${totalCount}`}
            </p>
            <div className="admin-pagination-controls">
              <button
                type="button"
                className="btn btn-secondary"
                disabled={page <= 1 || loading}
                onClick={() => setPage((p) => Math.max(1, p - 1))}
              >
                Previous
              </button>
              <span>
                Page {totalPages === 0 ? 0 : page} of {totalPages}
              </span>
              <button
                type="button"
                className="btn btn-secondary"
                disabled={page >= totalPages || loading || totalPages === 0}
                onClick={() => setPage((p) => p + 1)}
              >
                Next
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}
