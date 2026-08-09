import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { api } from '../api/client'
import { useCustomerAuth } from '../context/CustomerAuthContext'

function formatReviewDate(value) {
  if (!value) return ''
  try {
    return new Date(value).toLocaleDateString(undefined, {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
    })
  } catch {
    return ''
  }
}

function StarDisplay({ rating, size = 'md' }) {
  const value = Math.max(0, Math.min(5, Number(rating) || 0))
  const full = Math.round(value)
  return (
    <span className={`star-display star-display-${size}`} aria-label={`${value} out of 5 stars`}>
      {[1, 2, 3, 4, 5].map((n) => (
        <span key={n} className={n <= full ? 'is-on' : ''}>
          ★
        </span>
      ))}
    </span>
  )
}

function StarPicker({ value, onChange, disabled }) {
  return (
    <div className="star-picker" role="radiogroup" aria-label="Rating">
      {[1, 2, 3, 4, 5].map((n) => (
        <button
          key={n}
          type="button"
          role="radio"
          aria-checked={value === n}
          className={`star-picker-btn ${n <= value ? 'is-on' : ''}`}
          onClick={() => onChange(n)}
          disabled={disabled}
        >
          ★
        </button>
      ))}
    </div>
  )
}

export default function ProductReviews({ productId, onSummaryChange }) {
  const location = useLocation()
  const { isAuthenticated, loading: authLoading } = useCustomerAuth()
  const onSummaryChangeRef = useRef(onSummaryChange)
  onSummaryChangeRef.current = onSummaryChange
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [formError, setFormError] = useState('')
  const [success, setSuccess] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [reviews, setReviews] = useState([])
  const [averageRating, setAverageRating] = useState(0)
  const [reviewCount, setReviewCount] = useState(0)
  const [canReview, setCanReview] = useState(null)
  const [hasReviewed, setHasReviewed] = useState(null)
  const [myReview, setMyReview] = useState(null)
  const [editing, setEditing] = useState(false)
  const [rating, setRating] = useState(5)
  const [comment, setComment] = useState('')

  const load = useCallback(async () => {
    if (!productId) return
    setLoading(true)
    setError('')
    try {
      const data = await api.getProductReviews(productId)
      setReviews(data.reviews || [])
      setAverageRating(data.averageRating ?? 0)
      setReviewCount(data.reviewCount ?? 0)
      setCanReview(data.canReview ?? null)
      setHasReviewed(data.hasReviewed ?? null)
      setMyReview(data.myReview ?? null)
      onSummaryChangeRef.current?.({
        averageRating: data.averageRating ?? 0,
        reviewCount: data.reviewCount ?? 0,
      })
      if (data.myReview) {
        setRating(data.myReview.rating)
        setComment(data.myReview.comment || '')
        setEditing(false)
      } else {
        setRating(5)
        setComment('')
        setEditing(false)
      }
    } catch (err) {
      setError(err.message || 'Unable to load reviews.')
    } finally {
      setLoading(false)
    }
  }, [productId])

  useEffect(() => {
    load()
  }, [load, isAuthenticated])

  const handleSubmit = async (event) => {
    event.preventDefault()
    setFormError('')
    setSuccess('')
    setSubmitting(true)
    try {
      if (hasReviewed || myReview) {
        await api.updateMyProductReview(productId, { rating, comment })
        setSuccess('Your review was updated.')
      } else {
        await api.createProductReview(productId, { rating, comment })
        setSuccess('Thanks — your review was posted.')
      }
      await load()
    } catch (err) {
      setFormError(err.message || 'Unable to save review.')
    } finally {
      setSubmitting(false)
    }
  }

  const handleDelete = async () => {
    if (!window.confirm('Delete your review?')) return
    setFormError('')
    setSuccess('')
    setSubmitting(true)
    try {
      await api.deleteMyProductReview(productId)
      setSuccess('Your review was deleted.')
      await load()
    } catch (err) {
      setFormError(err.message || 'Unable to delete review.')
    } finally {
      setSubmitting(false)
    }
  }

  const showWriteForm =
    isAuthenticated && (canReview === true || ((hasReviewed || myReview) && editing))

  return (
    <section className="pdp-reviews" aria-labelledby="pdp-reviews-heading">
      <div className="pdp-reviews-header">
        <h2 id="pdp-reviews-heading">Reviews</h2>
        <div className="pdp-reviews-summary">
          {reviewCount > 0 ? (
            <>
              <StarDisplay rating={averageRating} />
              <span className="pdp-reviews-avg">{Number(averageRating).toFixed(1)}</span>
              <span className="pdp-reviews-count">
                ({reviewCount} {reviewCount === 1 ? 'review' : 'reviews'})
              </span>
            </>
          ) : (
            <span className="pdp-reviews-count">No reviews yet</span>
          )}
        </div>
      </div>

      {loading || authLoading ? (
        <p className="pdp-reviews-muted">Loading reviews…</p>
      ) : error ? (
        <p className="admin-error">{error}</p>
      ) : (
        <>
          {!isAuthenticated ? (
            <p className="pdp-reviews-prompt">
              <Link to="/login" state={{ from: location.pathname, message: 'Sign in to write a review.' }}>
                Sign in
              </Link>{' '}
              to write a review.
            </p>
          ) : null}

          {isAuthenticated && canReview === false && !hasReviewed && !myReview ? (
            <p className="pdp-reviews-prompt">Purchase this product to leave a review.</p>
          ) : null}

          {isAuthenticated && (hasReviewed || myReview) && !editing ? (
            <div className="pdp-my-review">
              <div className="pdp-my-review-top">
                <strong>Your review</strong>
                <div className="pdp-review-actions">
                  <button
                    type="button"
                    className="btn-text"
                    onClick={() => {
                      setEditing(true)
                      setRating(myReview?.rating || 5)
                      setComment(myReview?.comment || '')
                      setFormError('')
                      setSuccess('')
                    }}
                    disabled={submitting}
                  >
                    Edit
                  </button>
                  <button
                    type="button"
                    className="btn-text is-danger"
                    onClick={handleDelete}
                    disabled={submitting}
                  >
                    Delete
                  </button>
                </div>
              </div>
              <div className="pdp-review-item-top">
                <StarDisplay rating={myReview?.rating} />
                <span className="pdp-review-date">{formatReviewDate(myReview?.updatedAt || myReview?.createdAt)}</span>
              </div>
              {myReview?.comment ? <p className="pdp-review-comment">{myReview.comment}</p> : null}
            </div>
          ) : null}

          {showWriteForm ? (
            <form className="pdp-review-form" onSubmit={handleSubmit}>
              <h3>{myReview || hasReviewed ? 'Edit your review' : 'Write a review'}</h3>
              <div className="form-field">
                <label>Rating</label>
                <StarPicker value={rating} onChange={setRating} disabled={submitting} />
              </div>
              <div className="form-field">
                <label htmlFor="review-comment">
                  Comment <span className="optional">(optional)</span>
                </label>
                <textarea
                  id="review-comment"
                  rows={3}
                  maxLength={2000}
                  value={comment}
                  onChange={(e) => setComment(e.target.value)}
                  disabled={submitting}
                  placeholder="Share what you liked about this bag…"
                />
              </div>
              {formError ? <p className="admin-error">{formError}</p> : null}
              {success ? <p className="profile-success">{success}</p> : null}
              <div className="pdp-review-form-actions">
                <button type="submit" className="btn btn-primary" disabled={submitting || rating < 1}>
                  {submitting ? 'Saving…' : myReview || hasReviewed ? 'Save changes' : 'Post review'}
                </button>
                {editing ? (
                  <button
                    type="button"
                    className="btn btn-secondary"
                    onClick={() => {
                      setEditing(false)
                      setFormError('')
                      setRating(myReview?.rating || 5)
                      setComment(myReview?.comment || '')
                    }}
                    disabled={submitting}
                  >
                    Cancel
                  </button>
                ) : null}
              </div>
            </form>
          ) : null}

          {success && !showWriteForm ? <p className="profile-success">{success}</p> : null}
          {formError && !showWriteForm ? <p className="admin-error">{formError}</p> : null}

          <ul className="pdp-review-list">
            {reviews.filter((r) => !r.isMine).length === 0 && !(myReview || hasReviewed) ? (
              <li className="pdp-reviews-muted">Be the first to review this bag.</li>
            ) : (
              reviews
                .filter((r) => !r.isMine)
                .map((review) => (
                  <li key={review.id} className="pdp-review-item">
                    <div className="pdp-review-item-top">
                      <span className="pdp-reviewer">{review.reviewerName}</span>
                      <span className="pdp-review-date">{formatReviewDate(review.createdAt)}</span>
                    </div>
                    <StarDisplay rating={review.rating} size="sm" />
                    {review.comment ? <p className="pdp-review-comment">{review.comment}</p> : null}
                  </li>
                ))
            )}
          </ul>
        </>
      )}
    </section>
  )
}

export function CompactRating({ rating, reviews }) {
  const count = Number(reviews) || 0
  const value = Number(rating) || 0
  if (count <= 0 && value <= 0) {
    return <span className="product-rating-compact is-empty">No reviews</span>
  }
  const display = value > 0 ? value.toFixed(1) : '—'
  return (
    <span className="product-rating-compact" title={`${display} average from ${count} reviews`}>
      <span className="product-rating-star">★</span>
      <span>{display}</span>
      <span className="product-rating-count">({count})</span>
    </span>
  )
}
