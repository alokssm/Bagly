const FLY_SIZE = 12
const FLY_DURATION = 480

export function flyToCart(sourceRect) {
  const cartEl = document.getElementById('bagly-cart-icon')
  if (!cartEl || !sourceRect) return

  const cartRect = cartEl.getBoundingClientRect()
  const startX = sourceRect.left + sourceRect.width / 2 - FLY_SIZE / 2
  const startY = sourceRect.top + sourceRect.height / 2 - FLY_SIZE / 2
  const dx = cartRect.left + cartRect.width / 2 - (sourceRect.left + sourceRect.width / 2)
  const dy = cartRect.top + cartRect.height / 2 - (sourceRect.top + sourceRect.height / 2)

  const fly = document.createElement('div')
  fly.className = 'cart-fly-dot'
  fly.setAttribute('aria-hidden', 'true')
  fly.style.left = `${startX}px`
  fly.style.top = `${startY}px`
  document.body.appendChild(fly)

  const animation = fly.animate(
    [
      { transform: 'translate(0, 0) scale(1)', opacity: 1 },
      { transform: `translate(${dx}px, ${dy}px) scale(0.3)`, opacity: 0.7 },
    ],
    { duration: FLY_DURATION, easing: 'cubic-bezier(0.22, 1, 0.36, 1)', fill: 'forwards' },
  )

  animation.finished.then(() => fly.remove()).catch(() => fly.remove())
}

export function pulseAddButton(buttonEl) {
  if (!buttonEl) return

  buttonEl.classList.remove('btn-add-cart--tap')
  void buttonEl.offsetWidth
  buttonEl.classList.add('btn-add-cart--tap')

  const cleanup = () => buttonEl.classList.remove('btn-add-cart--tap')
  buttonEl.addEventListener('animationend', cleanup, { once: true })
}

export function handleCartAddAnimation(event) {
  flyToCart(event.detail?.sourceRect ?? null)
}
