function loadRazorpayScript() {
  if (window.Razorpay) return Promise.resolve(true)
  return new Promise((resolve) => {
    const existing = document.querySelector('script[data-bagly-razorpay]')
    if (existing) {
      existing.addEventListener('load', () => resolve(!!window.Razorpay))
      existing.addEventListener('error', () => resolve(false))
      return
    }
    const script = document.createElement('script')
    script.src = 'https://checkout.razorpay.com/v1/checkout.js'
    script.async = true
    script.dataset.baglyRazorpay = '1'
    script.onload = () => resolve(!!window.Razorpay)
    script.onerror = () => resolve(false)
    document.body.appendChild(script)
  })
}

export async function openRazorpayCheckout(options) {
  const ready = await loadRazorpayScript()
  if (!ready) {
    throw new Error('Unable to load Razorpay checkout. Check your network connection.')
  }

  return new Promise((resolve, reject) => {
    const rzp = new window.Razorpay({
      ...options,
      handler: (response) => resolve(response),
      modal: {
        ondismiss: () => reject(new Error('Payment cancelled.')),
      },
    })

    rzp.on('payment.failed', (response) => {
      const err = response?.error || {}
      reject(Object.assign(new Error(err.description || 'Payment failed.'), { razorpayError: err }))
    })

    rzp.open()
  })
}
