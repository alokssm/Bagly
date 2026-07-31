import { useEffect, useRef, useState } from 'react'
import { useChatHub } from '../hooks/useChatHub'
import '../styles/chat.css'

const GREETING = {
  id: 'greeting',
  role: 'assistant',
  content:
    "Hi! I'm the Bagly assistant. I can check product stock, set a restock alert, or look up an order (order number + email). How can I help?",
  timestamp: new Date().toISOString(),
}

function formatTime(value) {
  try {
    return new Date(value).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })
  } catch {
    return ''
  }
}

export default function ChatWidget({ token }) {
  const [open, setOpen] = useState(false)
  const [draft, setDraft] = useState('')
  const { messages, status, isTyping, error, sendMessage } = useChatHub(token)
  const listRef = useRef(null)

  const allMessages = [GREETING, ...messages]

  useEffect(() => {
    if (!open) return
    const el = listRef.current
    if (el) el.scrollTop = el.scrollHeight
  }, [allMessages.length, isTyping, open])

  const onSubmit = (e) => {
    e.preventDefault()
    if (!draft.trim()) return
    sendMessage(draft)
    setDraft('')
  }

  return (
    <div className="chat-widget">
      {open ? (
        <div className="chat-panel" role="dialog" aria-label="Bagly assistant chat">
          <div className="chat-panel-head">
            <div>
              <strong>Bagly Assistant</strong>
              <span className={`chat-status chat-status-${status}`}>
                {status === 'connected' ? 'Online' : status === 'reconnecting' ? 'Reconnecting…' : 'Connecting…'}
              </span>
            </div>
            <button type="button" className="chat-close" aria-label="Close chat" onClick={() => setOpen(false)}>
              ✕
            </button>
          </div>

          <div className="chat-messages" ref={listRef}>
            {allMessages.map((message) => (
              <div key={message.id} className={`chat-bubble chat-bubble-${message.role}`}>
                <p>{message.content}</p>
                <time>{formatTime(message.timestamp)}</time>
              </div>
            ))}
            {isTyping ? (
              <div className="chat-bubble chat-bubble-assistant chat-typing">
                <span />
                <span />
                <span />
              </div>
            ) : null}
          </div>

          {error ? <p className="chat-error">{error}</p> : null}

          <form className="chat-input-row" onSubmit={onSubmit}>
            <input
              type="text"
              value={draft}
              onChange={(e) => setDraft(e.target.value)}
              placeholder="Ask about stock, orders, or restock alerts…"
              maxLength={1000}
              aria-label="Message"
            />
            <button type="submit" className="btn btn-primary" disabled={!draft.trim()}>
              Send
            </button>
          </form>
        </div>
      ) : null}

      <button
        type="button"
        className="chat-toggle"
        onClick={() => setOpen((v) => !v)}
        aria-label={open ? 'Close chat' : 'Open chat with Bagly assistant'}
      >
        {open ? '✕' : '💬'}
      </button>
    </div>
  )
}
