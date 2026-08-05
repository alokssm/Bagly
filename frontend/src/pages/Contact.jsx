import { useState } from 'react'
import { api } from '../api/client'
import { buildContactPayload } from '../utils/payloads'
import ApiErrorState from '../components/ApiErrorState'

const initialForm = {
  firstName: '',
  lastName: '',
  phone: '',
  email: '',
  companyName: '',
  message: '',
}

export default function Contact() {
  const [form, setForm] = useState(initialForm)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState('')
  const [success, setSuccess] = useState('')

  const onChange = (e) => {
    const { name, value } = e.target
    setForm((prev) => ({ ...prev, [name]: value }))
  }

  const submitMessage = async () => {
    if (submitting) return

    setSubmitting(true)
    setError('')
    setSuccess('')

    try {
      const payload = buildContactPayload(form)
      const result = await api.submitContactForm(payload)
      setSuccess(result?.message || "Thanks for reaching out — we'll get back to you soon.")
      setForm(initialForm)
    } catch (err) {
      setError(err.message || 'Unable to send your message. Please try again.')
    } finally {
      setSubmitting(false)
    }
  }

  const onSubmit = (e) => {
    e.preventDefault()
    submitMessage()
  }

  return (
    <section className="section" style={{ paddingTop: 0 }}>
      <div className="container">
        <div className="page-hero">
          <span className="eyebrow">Contact</span>
          <h1>We'd love to hear from you</h1>
          <p>
            Questions about an order, a bulk/corporate inquiry, or just want to say hello?
            Send us a note and our team will get back to you shortly.
          </p>
        </div>

        <div className="checkout-layout">
          <div className="form-card">
            <h2>Send a message</h2>

            {success ? (
              <div className="success-banner" role="status">
                <h2>Message sent</h2>
                <p>{success}</p>
              </div>
            ) : (
              <form onSubmit={onSubmit}>
                {error ? (
                  <ApiErrorState
                    title="Couldn't send your message"
                    message={error}
                    onRetry={submitMessage}
                    compact
                    className="contact-form-error"
                  />
                ) : null}

                <div className="form-grid">
                  <div className="form-field">
                    <label htmlFor="firstName">First name</label>
                    <input
                      id="firstName"
                      name="firstName"
                      required
                      maxLength={100}
                      value={form.firstName}
                      onChange={onChange}
                      disabled={submitting}
                    />
                  </div>
                  <div className="form-field">
                    <label htmlFor="lastName">Last name</label>
                    <input
                      id="lastName"
                      name="lastName"
                      required
                      maxLength={100}
                      value={form.lastName}
                      onChange={onChange}
                      disabled={submitting}
                    />
                  </div>
                  <div className="form-field">
                    <label htmlFor="phone">Phone number</label>
                    <input
                      id="phone"
                      name="phone"
                      type="tel"
                      required
                      maxLength={30}
                      value={form.phone}
                      onChange={onChange}
                      disabled={submitting}
                      placeholder="Phone"
                    />
                  </div>
                  <div className="form-field">
                    <label htmlFor="email">Email</label>
                    <input
                      id="email"
                      name="email"
                      type="email"
                      required
                      maxLength={256}
                      value={form.email}
                      onChange={onChange}
                      disabled={submitting}
                      placeholder="Email"
                    />
                  </div>
                  <div className="form-field full">
                    <label htmlFor="companyName">Company name (optional)</label>
                    <input
                      id="companyName"
                      name="companyName"
                      maxLength={200}
                      value={form.companyName}
                      onChange={onChange}
                      disabled={submitting}
                    />
                  </div>
                  <div className="form-field full">
                    <label htmlFor="message">Message</label>
                    <textarea
                      id="message"
                      name="message"
                      required
                      maxLength={4000}
                      value={form.message}
                      onChange={onChange}
                      disabled={submitting}
                      placeholder="Tell us how we can help…"
                    />
                  </div>
                </div>

                <button type="submit" className="btn btn-brass btn-block" disabled={submitting}>
                  {submitting ? 'Sending…' : 'Send message'}
                </button>
              </form>
            )}
          </div>

          <aside className="cart-summary contact-info">
            <h2>Get in touch</h2>
            <p>
              Our team typically responds within 1–2 business days. For order-specific questions,
              include your order number so we can help faster.
            </p>
            <div className="contact-info__item">
              <strong>Email</strong>
              <a href="mailto:hello@bagly.store">hello@bagly.store</a>
            </div>
            <div className="contact-info__item">
              <strong>Shipping</strong>
              Free shipping over ₹10000 · 10-day returns
            </div>
          </aside>
        </div>
      </div>
    </section>
  )
}
