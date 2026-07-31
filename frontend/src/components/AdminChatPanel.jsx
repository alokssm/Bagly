import { useEffect, useMemo, useRef, useState } from 'react'
import { useAdminChatHub } from '../hooks/useAdminChatHub'
import '../styles/admin-chat.css'

function formatTime(value) {
  try {
    return new Date(value).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })
  } catch {
    return ''
  }
}

function formatWhen(value) {
  if (!value) return ''
  try {
    const date = new Date(value)
    const now = new Date()
    const sameDay = date.toDateString() === now.toDateString()
    return sameDay
      ? date.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })
      : date.toLocaleDateString([], { month: 'short', day: 'numeric' })
  } catch {
    return ''
  }
}

export default function AdminChatPanel({ token }) {
  const [open, setOpen] = useState(false)
  const [selectedId, setSelectedId] = useState(null)
  const [draft, setDraft] = useState('')
  const {
    conversations,
    status,
    error,
    messagesByCustomer,
    loadHistory,
    joinConversation,
    leaveConversation,
    sendMessage,
  } = useAdminChatHub(token)
  const listRef = useRef(null)
  const loadedRef = useRef(new Set())

  const selected = useMemo(
    () => conversations.find((c) => c.customerId === selectedId) || null,
    [conversations, selectedId],
  )
  const messages = messagesByCustomer[selectedId] || []
  const onlineCount = conversations.filter((c) => c.isOnline).length

  useEffect(() => {
    const el = listRef.current
    if (el) el.scrollTop = el.scrollHeight
  }, [messages.length])

  useEffect(() => {
    if (!selectedId || loadedRef.current.has(selectedId)) return
    loadedRef.current.add(selectedId)
    loadHistory(selectedId)
  }, [selectedId, loadHistory])

  const handleSelect = (customerId) => {
    setSelectedId(customerId)
  }

  const handleJoin = () => {
    if (!selectedId) return
    joinConversation(selectedId)
  }

  const handleLeave = () => {
    if (!selectedId) return
    leaveConversation(selectedId)
  }

  const onSubmit = (e) => {
    e.preventDefault()
    if (!draft.trim() || !selectedId) return
    sendMessage(selectedId, draft)
    setDraft('')
  }

  if (!token) return null

  return (
    <div className="admin-chat-widget">
      {open ? (
        <div className="admin-chat-panel" role="dialog" aria-label="Customer support chat">
          <div className="admin-chat-head">
            <div>
              <strong>Customer Chat</strong>
              <span className={`chat-status chat-status-${status}`}>
                {status === 'connected'
                  ? `Online · ${onlineCount} customer${onlineCount === 1 ? '' : 's'} online`
                  : status === 'reconnecting'
                    ? 'Reconnecting…'
                    : 'Connecting…'}
              </span>
            </div>
            <button type="button" className="chat-close" aria-label="Close chat" onClick={() => setOpen(false)}>
              ✕
            </button>
          </div>

          {error ? <p className="chat-error">{error}</p> : null}

          <div className="admin-chat-body">
            <div className="admin-chat-conversations">
              {conversations.length === 0 ? (
                <p className="admin-chat-empty">No customer conversations yet.</p>
              ) : (
                conversations.map((c) => (
                  <button
                    key={c.customerId}
                    type="button"
                    className={`admin-chat-conversation ${c.customerId === selectedId ? 'is-active' : ''}`}
                    onClick={() => handleSelect(c.customerId)}
                  >
                    <div className="admin-chat-conversation-top">
                      <span className={`admin-chat-dot ${c.isOnline ? 'is-online' : ''}`} />
                      <strong>{c.name}</strong>
                      <time>{formatWhen(c.lastAt)}</time>
                    </div>
                    <p className="admin-chat-conversation-snippet">{c.lastMessage || c.email}</p>
                    {c.isJoined ? <span className="admin-chat-badge">{c.joinedAdminName || 'Joined'}</span> : null}
                  </button>
                ))
              )}
            </div>

            <div className="admin-chat-thread">
              {selected ? (
                <>
                  <div className="admin-chat-thread-head">
                    <div>
                      <strong>{selected.name}</strong>
                      <small>{selected.email}</small>
                    </div>
                    {selected.isJoined ? (
                      <button type="button" className="btn btn-secondary btn-sm" onClick={handleLeave}>
                        Leave
                      </button>
                    ) : (
                      <button type="button" className="btn btn-primary btn-sm" onClick={handleJoin}>
                        Join
                      </button>
                    )}
                  </div>

                  <div className="chat-messages admin-chat-messages" ref={listRef}>
                    {messages.length === 0 ? (
                      <p className="admin-chat-empty">No messages yet.</p>
                    ) : (
                      messages.map((message) => (
                        <div key={message.id} className={`chat-bubble chat-bubble-${message.role}`}>
                          <p>{message.content}</p>
                          <time>{formatTime(message.timestamp)}</time>
                        </div>
                      ))
                    )}
                  </div>

                  <form className="chat-input-row" onSubmit={onSubmit}>
                    <input
                      type="text"
                      value={draft}
                      onChange={(e) => setDraft(e.target.value)}
                      placeholder={selected.isJoined ? 'Reply to customer…' : 'Type to reply and join…'}
                      maxLength={1000}
                      aria-label="Message"
                    />
                    <button type="submit" className="btn btn-primary" disabled={!draft.trim()}>
                      Send
                    </button>
                  </form>
                </>
              ) : (
                <p className="admin-chat-empty admin-chat-placeholder">
                  Select a conversation to view messages.
                </p>
              )}
            </div>
          </div>
        </div>
      ) : null}

      <button
        type="button"
        className="chat-toggle admin-chat-toggle"
        onClick={() => setOpen((v) => !v)}
        aria-label={open ? 'Close support chat' : 'Open support chat'}
      >
        {open ? '✕' : '💬'}
        {!open && onlineCount > 0 ? <span className="admin-chat-toggle-badge">{onlineCount}</span> : null}
      </button>
    </div>
  )
}
