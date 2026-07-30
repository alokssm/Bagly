import { useEffect, useState } from 'react'
import { api } from '../../api/client'
import { buildUpsertCategoryPayload } from '../../utils/payloads'

const emptyForm = { id: '', label: '', sortOrder: 0 }

export default function AdminCategories() {
  const [categories, setCategories] = useState([])
  const [form, setForm] = useState(emptyForm)
  const [editingId, setEditingId] = useState(null)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  const load = async () => {
    setLoading(true)
    setError('')
    try {
      const data = await api.adminGetCategories()
      setCategories(data)
    } catch (err) {
      setError(err.message || 'Unable to load categories.')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    load()
  }, [])

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
    })
  }

  const onChange = (e) => {
    const { name, value } = e.target
    setForm((prev) => ({
      ...prev,
      [name]: name === 'sortOrder' ? Number(value) : value,
    }))
  }

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
      await load()
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
      await load()
    } catch (err) {
      setError(err.message || 'Delete failed.')
    }
  }

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
          </div>
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

        <div className="admin-table-wrap">
          {loading ? <p>Loading…</p> : null}
          <table className="admin-table">
            <thead>
              <tr>
                <th>ID</th>
                <th>Label</th>
                <th>Sort</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {categories.map((category) => (
                <tr key={category.id}>
                  <td>{category.id}</td>
                  <td>{category.label}</td>
                  <td>{category.sortOrder}</td>
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
        </div>
      </div>
    </div>
  )
}
