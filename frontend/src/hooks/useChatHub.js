import { useCallback, useEffect, useRef, useState } from 'react'
import * as signalR from '@microsoft/signalr'
import { getHubBase } from '../api/client'

const SESSION_KEY = 'bagly-chat-session-id'

function getOrCreateSessionId() {
  try {
    const existing = localStorage.getItem(SESSION_KEY)
    if (existing) return existing
    const created =
      typeof crypto !== 'undefined' && crypto.randomUUID
        ? crypto.randomUUID()
        : `chat-${Date.now()}-${Math.random().toString(36).slice(2)}`
    localStorage.setItem(SESSION_KEY, created)
    return created
  } catch {
    return `chat-${Date.now()}-${Math.random().toString(36).slice(2)}`
  }
}

let idCounter = 0
function nextId() {
  idCounter += 1
  return `msg-${Date.now()}-${idCounter}`
}

/** Connects to the Bagly chat hub (requires a customer/admin JWT) and exposes message state + a send function. */
export function useChatHub(token) {
  const [messages, setMessages] = useState([])
  const [status, setStatus] = useState('connecting') // connecting | connected | reconnecting | disconnected
  const [isTyping, setIsTyping] = useState(false)
  const [error, setError] = useState('')
  const connectionRef = useRef(null)
  const sessionIdRef = useRef(getOrCreateSessionId())

  useEffect(() => {
    if (!token) {
      setStatus('disconnected')
      return
    }

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${getHubBase()}/chat`, { accessTokenFactory: () => token })
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Warning)
      .build()

    connectionRef.current = connection

    connection.on('ReceiveMessage', (message) => {
      setIsTyping(false)
      setMessages((prev) => [
        ...prev,
        {
          id: nextId(),
          role: message?.role || 'assistant',
          content: message?.content || '',
          timestamp: message?.timestamp || new Date().toISOString(),
        },
      ])
    })

    connection.on('ReceiveTyping', (typing) => setIsTyping(Boolean(typing)))

    connection.on('ReceiveError', (message) => {
      setIsTyping(false)
      setError(message || 'Something went wrong.')
    })

    connection.onreconnecting(() => setStatus('reconnecting'))
    connection.onreconnected(() => {
      setStatus('connected')
      connection.invoke('Join', sessionIdRef.current).catch(() => {})
    })
    connection.onclose(() => setStatus('disconnected'))

    connection
      .start()
      .then(() => {
        setStatus('connected')
        return connection.invoke('Join', sessionIdRef.current)
      })
      .catch(() => {
        setStatus('disconnected')
        setError('Unable to connect to chat right now.')
      })

    return () => {
      connection.stop()
    }
  }, [token])

  const sendMessage = useCallback((text) => {
    const trimmed = text.trim()
    if (!trimmed) return

    setError('')
    setMessages((prev) => [
      ...prev,
      { id: nextId(), role: 'user', content: trimmed, timestamp: new Date().toISOString() },
    ])

    const connection = connectionRef.current
    if (!connection || connection.state !== signalR.HubConnectionState.Connected) {
      setError('Not connected yet — please try again in a moment.')
      return
    }

    connection.invoke('SendMessage', sessionIdRef.current, trimmed).catch(() => {
      setError('Failed to send message. Please try again.')
      setIsTyping(false)
    })
  }, [])

  return { messages, status, isTyping, error, sendMessage }
}
