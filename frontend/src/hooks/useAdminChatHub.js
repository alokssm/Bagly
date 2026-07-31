import { useCallback, useEffect, useRef, useState } from 'react'
import * as signalR from '@microsoft/signalr'
import { getHubBase } from '../api/client'

let idCounter = 0
function nextId() {
  idCounter += 1
  return `admin-msg-${Date.now()}-${idCounter}`
}

function toMessage(raw) {
  return {
    id: nextId(),
    role: raw?.role || 'user',
    content: raw?.content || '',
    timestamp: raw?.timestamp || new Date().toISOString(),
  }
}

function upsertConversation(list, next) {
  const idx = list.findIndex((c) => c.customerId === next.customerId)
  const merged = [...list]
  if (idx === -1) {
    merged.push(next)
  } else {
    merged[idx] = next
  }
  return merged.sort((a, b) => new Date(b.lastAt || 0) - new Date(a.lastAt || 0))
}

/** Connects to the Bagly chat hub as an admin: conversation list + per-conversation message threads. */
export function useAdminChatHub(token) {
  const [conversations, setConversations] = useState([])
  const [status, setStatus] = useState('connecting') // connecting | connected | reconnecting | disconnected
  const [error, setError] = useState('')
  const [messagesByCustomer, setMessagesByCustomer] = useState({})
  const connectionRef = useRef(null)

  const refreshConversations = useCallback(async () => {
    const connection = connectionRef.current
    if (!connection || connection.state !== signalR.HubConnectionState.Connected) return
    try {
      const list = await connection.invoke('GetActiveConversations')
      setConversations(Array.isArray(list) ? [...list].sort((a, b) => new Date(b.lastAt || 0) - new Date(a.lastAt || 0)) : [])
    } catch {
      setError('Unable to load conversations.')
    }
  }, [])

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

    connection.on('ReceiveConversationUpdated', (conversation) => {
      if (!conversation) return
      setConversations((prev) => upsertConversation(prev, conversation))
    })

    connection.on('ReceiveConversationMessage', (message) => {
      if (!message?.customerId) return
      setMessagesByCustomer((prev) => ({
        ...prev,
        [message.customerId]: [...(prev[message.customerId] || []), toMessage(message)],
      }))
    })

    connection.on('ReceiveError', (message) => setError(message || 'Something went wrong.'))

    connection.onreconnecting(() => setStatus('reconnecting'))
    connection.onreconnected(() => {
      setStatus('connected')
      refreshConversations()
    })
    connection.onclose(() => setStatus('disconnected'))

    connection
      .start()
      .then(() => {
        setStatus('connected')
        return refreshConversations()
      })
      .catch(() => {
        setStatus('disconnected')
        setError('Unable to connect to chat right now.')
      })

    return () => {
      connection.stop()
    }
  }, [token, refreshConversations])

  const loadHistory = useCallback(async (customerId) => {
    const connection = connectionRef.current
    if (!connection || connection.state !== signalR.HubConnectionState.Connected) return
    try {
      const history = await connection.invoke('GetConversationHistory', customerId)
      setMessagesByCustomer((prev) => ({
        ...prev,
        [customerId]: Array.isArray(history) ? history.map(toMessage) : [],
      }))
    } catch {
      // Non-fatal — thread just stays empty until a live message arrives.
    }
  }, [])

  const joinConversation = useCallback(async (customerId) => {
    const connection = connectionRef.current
    if (!connection || connection.state !== signalR.HubConnectionState.Connected) return
    setError('')
    try {
      const history = await connection.invoke('JoinConversation', customerId)
      setMessagesByCustomer((prev) => ({
        ...prev,
        [customerId]: Array.isArray(history) ? history.map(toMessage) : [],
      }))
    } catch {
      setError('Unable to join this conversation.')
    }
  }, [])

  const leaveConversation = useCallback((customerId) => {
    const connection = connectionRef.current
    if (!connection || connection.state !== signalR.HubConnectionState.Connected) return
    connection.invoke('LeaveConversation', customerId).catch(() => {})
  }, [])

  const sendMessage = useCallback((customerId, text) => {
    const trimmed = text.trim()
    if (!trimmed) return
    const connection = connectionRef.current
    if (!connection || connection.state !== signalR.HubConnectionState.Connected) {
      setError('Not connected yet — please try again in a moment.')
      return
    }
    setError('')
    connection.invoke('SendAdminMessage', customerId, trimmed).catch(() => {
      setError('Failed to send message. Please try again.')
    })
  }, [])

  return {
    conversations,
    status,
    error,
    messagesByCustomer,
    loadHistory,
    joinConversation,
    leaveConversation,
    sendMessage,
    refreshConversations,
  }
}
