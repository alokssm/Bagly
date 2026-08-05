export default function ApiErrorState({
  title = 'Something went wrong',
  message = "We're having trouble reaching Bagly right now. Please try again in a moment.",
  onRetry,
  compact = false,
  className = '',
  children,
}) {
  return (
    <div
      className={`async-state error-state${compact ? ' async-state--compact' : ''}${className ? ` ${className}` : ''}`}
      role="alert"
    >
      <div className="async-state__icon" aria-hidden="true">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75">
          <circle cx="12" cy="12" r="9" />
          <line x1="12" y1="8" x2="12" y2="12" />
          <circle cx="12" cy="16" r="0.5" fill="currentColor" stroke="none" />
        </svg>
      </div>
      {!compact ? <h2 className="async-state__title">{title}</h2> : null}
      <p className="async-state__message">{message}</p>
      {onRetry || children ? (
        <div className="async-state__actions">
          {onRetry ? (
            <button type="button" className="btn btn-primary" onClick={onRetry}>
              Try again
            </button>
          ) : null}
          {children}
        </div>
      ) : null}
    </div>
  )
}
