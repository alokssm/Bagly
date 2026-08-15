import { useCallback, useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api } from '../api/client'
import LoadingState from '../components/LoadingState'
import ApiErrorState from '../components/ApiErrorState'

const TRACKING_STEPS = [
  'PICKUP_REQUESTED',
  'PICKED_UP',
  'IN_TRANSIT',
  'OUT_FOR_DELIVERY',
  'DELIVERED',
]

const STATUS_LABELS = {
  PICKUP_REQUESTED: 'Pickup requested',
  PICKED_UP: 'Picked up',
  IN_TRANSIT: 'In transit',
  OUT_FOR_DELIVERY: 'Out for delivery',
  DELIVERED: 'Delivered',
}

function formatTrackingStatus(status) {
  if (!status) return null
  return STATUS_LABELS[status] || String(status).replaceAll('_', ' ')
}

function formatWhen(value) {
  if (!value) return null
  try {
    return new Date(value).toLocaleString('en-IN', {
      day: 'numeric',
      month: 'short',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    })
  } catch {
    return null
  }
}

function stepIndex(status) {
  if (!status) return -1
  return TRACKING_STEPS.indexOf(status)
}

function CarrierTruckIcon() {
  return (
    <svg
      className="order-track-timeline__truck-svg"
      viewBox="0 0 28 16"
      width="31"
      height="18"
      aria-hidden="true"
      focusable="false"
    >
      <path
        fill="currentColor"
        d="M1.5 3.2h12.2c.4 0 .7.3.7.7v7.4H1.5V3.2zm13.6 2.4h5.1c.3 0 .5.1.7.3l3.2 3.1c.2.2.3.4.3.7v1.6h-1.1a2.35 2.35 0 0 0-4.5 0h-2.2a2.35 2.35 0 0 0-4.5 0H6.4a2.35 2.35 0 0 0-4.5 0H1.5V9.2h13.6V5.6z"
      />
      <circle cx="6.4" cy="12.2" r="1.85" fill="none" stroke="currentColor" strokeWidth="1.4" />
      <circle cx="18.2" cy="12.2" r="1.85" fill="none" stroke="currentColor" strokeWidth="1.4" />
      <circle cx="22.7" cy="12.2" r="1.85" fill="none" stroke="currentColor" strokeWidth="1.4" />
      <path
        fill="none"
        stroke="currentColor"
        strokeWidth="1.2"
        strokeLinejoin="round"
        d="M15.1 5.6v3.6h6.4"
      />
    </svg>
  )
}

function ShipmentTrackCard({ shipment, index, total }) {
  const currentIdx = stepIndex(shipment.trackingStatus)
  const history = Array.isArray(shipment.statusHistory) ? shipment.statusHistory : []
  const title = total > 1 ? `Shipment ${index + 1}` : 'Shipment'

  if (!shipment.canTrack) {
    return (
      <div className="order-track-shipment">
        <div className="order-track-shipment__head">
          <h2>{title}</h2>
          <p className="order-track-muted">Tracking unavailable — shipping has not started yet.</p>
        </div>
      </div>
    )
  }

  return (
    <div className="order-track-shipment">
      <div className="order-track-shipment__head">
        <h2>{title}</h2>
        <div className="order-track-shipment__meta">
          {shipment.trackingStatus ? (
            <span className="order-status order-status--shipped">
              {formatTrackingStatus(shipment.trackingStatus)}
            </span>
          ) : (
            <span className="order-status">Ready to track</span>
          )}
        </div>
      </div>

      <ol className="order-track-timeline" aria-label="Shipment status timeline">
        {TRACKING_STEPS.map((step, i) => {
          const done = currentIdx >= i
          const current = currentIdx === i
          const hist = [...history].reverse().find((e) => e.status === step)
          return (
            <li
              key={step}
              className={`order-track-timeline__step${done ? ' is-done' : ''}${current ? ' is-current' : ''}`}
            >
              {current ? (
                <span className="order-track-timeline__truck" aria-hidden="true" title="Current status">
                  <CarrierTruckIcon />
                </span>
              ) : null}
              <span className="order-track-timeline__dot" aria-hidden="true" />
              <div className="order-track-timeline__body">
                <span className="order-track-timeline__label">{formatTrackingStatus(step)}</span>
                <span className="order-track-timeline__when">
                  {hist?.changedAtUtc ? formatWhen(hist.changedAtUtc) : '—'}
                </span>
              </div>
            </li>
          )
        })}
      </ol>
    </div>
  )
}

export default function OrderTrack() {
  const { orderNumber } = useParams()
  const [data, setData] = useState(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const load = useCallback(async () => {
    if (!orderNumber) return
    setLoading(true)
    setError('')
    try {
      const track = await api.getMyOrderTrack(orderNumber)
      setData(track)
    } catch (err) {
      setError(err.message || 'Unable to load tracking.')
      setData(null)
    } finally {
      setLoading(false)
    }
  }, [orderNumber])

  useEffect(() => {
    load()
  }, [load])

  if (loading) {
    return (
      <div className="container">
        <LoadingState message="Loading tracking…" />
      </div>
    )
  }

  if (error) {
    return (
      <div className="container">
        <ApiErrorState title="Couldn't load tracking" message={error} onRetry={load}>
          <Link to="/orders" className="btn btn-secondary">
            Back to orders
          </Link>
        </ApiErrorState>
      </div>
    )
  }

  const shipments = data?.shipments || []

  return (
    <section className="section" style={{ paddingTop: 0 }}>
      <div className="container order-track">
        <div className="page-hero">
          <Link to="/orders" className="order-track-back">
            ← My orders
          </Link>
          <span className="eyebrow">Tracking</span>
          <h1>{data?.orderNumber || orderNumber}</h1>
          <p className="order-track-lead">
            {data?.canTrack
              ? 'Live shipping status for this order.'
              : 'Tracking will appear once shipping has started.'}
          </p>
        </div>

        {!data?.canTrack || shipments.length === 0 ? (
          <div className="order-track-empty">
            <p>Tracking unavailable for this order yet.</p>
            <Link to="/orders" className="btn btn-secondary">
              Back to orders
            </Link>
          </div>
        ) : (
          <div className="order-track-list">
            {shipments.map((shipment, index) => (
              <ShipmentTrackCard
                key={shipment.id}
                shipment={shipment}
                index={index}
                total={shipments.length}
              />
            ))}
          </div>
        )}
      </div>
    </section>
  )
}
