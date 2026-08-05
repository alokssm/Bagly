export default function LoadingState({ message = 'Loading…', compact = false, className = '' }) {
  return (
    <div
      className={`async-state loading-state${compact ? ' async-state--compact' : ''}${className ? ` ${className}` : ''}`}
      role="status"
      aria-live="polite"
      aria-busy="true"
    >
      <div className="async-state__spinner" aria-hidden="true" />
      <p className="async-state__message">{message}</p>
    </div>
  )
}
