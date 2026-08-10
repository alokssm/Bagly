export default function ProductSearchBar({
  id = 'product-search',
  value,
  onChange,
  onSubmit,
  placeholder = 'Search bags…',
  className = '',
}) {
  const handleSubmit = (e) => {
    e.preventDefault()
    onSubmit?.(value.trim())
  }

  return (
    <form
      className={`product-search ${className}`.trim()}
      role="search"
      onSubmit={handleSubmit}
    >
      <label className="product-search-label" htmlFor={id}>
        Search products
      </label>
      <div className="product-search-field">
        <svg
          className="product-search-icon"
          width="18"
          height="18"
          viewBox="0 0 24 24"
          fill="none"
          aria-hidden="true"
        >
          <circle cx="11" cy="11" r="7" stroke="currentColor" strokeWidth="1.75" />
          <path d="M16.5 16.5 21 21" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" />
        </svg>
        <input
          id={id}
          type="search"
          name="q"
          value={value}
          onChange={(e) => onChange(e.target.value)}
          placeholder={placeholder}
          autoComplete="off"
          enterKeyHint="search"
        />
        <button type="submit" className="product-search-btn" aria-label="Search">
          <svg
            className="product-search-btn-icon"
            width="18"
            height="18"
            viewBox="0 0 24 24"
            fill="none"
            aria-hidden="true"
          >
            <circle cx="11" cy="11" r="7" stroke="currentColor" strokeWidth="1.75" />
            <path
              d="M16.5 16.5 21 21"
              stroke="currentColor"
              strokeWidth="1.75"
              strokeLinecap="round"
            />
          </svg>
        </button>
      </div>
    </form>
  )
}
