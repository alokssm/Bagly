import * as XLSX from 'xlsx'
import { jsPDF } from 'jspdf'
import autoTable from 'jspdf-autotable'

function downloadBlob(blob, filename) {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  a.rel = 'noopener'
  document.body.appendChild(a)
  a.click()
  // Delay revoke — some browsers cancel the download if the blob URL is revoked immediately.
  window.setTimeout(() => {
    a.remove()
    URL.revokeObjectURL(url)
  }, 1500)
}

function stamp() {
  const d = new Date()
  const pad = (n) => String(n).padStart(2, '0')
  return `${d.getFullYear()}${pad(d.getMonth() + 1)}${pad(d.getDate())}-${pad(d.getHours())}${pad(d.getMinutes())}`
}

/**
 * @param {object} options
 * @param {string} options.filenameBase
 * @param {string[]} options.headers
 * @param {Array<Array<string|number>>} options.rows
 * @param {string} [options.sheetName]
 */
export function exportRowsToExcel({ filenameBase, headers, rows, sheetName = 'Orders' }) {
  const data = [headers, ...rows]
  const sheet = XLSX.utils.aoa_to_sheet(data)
  const book = XLSX.utils.book_new()
  XLSX.utils.book_append_sheet(book, sheet, sheetName)
  // SheetJS 0.18+ returns ArrayBuffer for type:'array' — wrap for reliable Blob downloads.
  const raw = XLSX.write(book, { bookType: 'xlsx', type: 'array' })
  const bytes = raw instanceof ArrayBuffer ? new Uint8Array(raw) : raw
  downloadBlob(
    new Blob([bytes], {
      type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    }),
    `${filenameBase}-${stamp()}.xlsx`,
  )
}

/**
 * @param {object} options
 * @param {string} options.filenameBase
 * @param {string} options.title
 * @param {string} [options.subtitle]
 * @param {string[]} options.headers
 * @param {Array<Array<string|number>>} options.rows
 */
export function exportRowsToPdf({ filenameBase, title, subtitle, headers, rows }) {
  const doc = new jsPDF({ orientation: 'landscape', unit: 'pt', format: 'a4' })
  doc.setFontSize(14)
  doc.text(title, 40, 36)
  if (subtitle) {
    doc.setFontSize(9)
    doc.setTextColor(90)
    doc.text(subtitle, 40, 52)
    doc.setTextColor(0)
  }
  autoTable(doc, {
    startY: subtitle ? 62 : 48,
    head: [headers],
    body: rows,
    styles: { fontSize: 7, cellPadding: 3, overflow: 'linebreak' },
    headStyles: { fillColor: [27, 61, 47], textColor: 255 },
    margin: { left: 28, right: 28 },
  })
  doc.save(`${filenameBase}-${stamp()}.pdf`)
}

function formatWhen(value) {
  if (!value) return ''
  try {
    return new Date(value).toLocaleString(undefined, {
      day: 'numeric',
      month: 'short',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    })
  } catch {
    return String(value)
  }
}

function money(value) {
  const n = Number(value)
  if (!Number.isFinite(n)) return ''
  return n.toFixed(2)
}

function sellerItemsSummary(order) {
  return (order.items || [])
    .map((i) => {
      const color = i.color ? ` (${i.color})` : ''
      return `${i.productName}${color} ×${i.quantity}`
    })
    .join('; ')
}

/** Flatten seller report orders into export rows. */
export function sellerOrdersToExportRows(orders) {
  const headers = [
    'Order #',
    'Date',
    'Status',
    'Customer',
    'Items',
    'Your Subtotal',
    'Payment',
    'Provider',
    'City',
    'State',
    'ZIP',
  ]
  const rows = (orders || []).map((o) => [
    o.orderNumber || '',
    formatWhen(o.createdAt),
    o.status || '',
    o.customerName || '',
    sellerItemsSummary(o),
    money(o.subtotal),
    o.paymentStatus || '',
    o.paymentProvider || '',
    o.city || '',
    o.state || '',
    o.zip || '',
  ])
  return { headers, rows }
}

/** Flatten admin order list items into export rows. */
export function adminOrdersToExportRows(orders) {
  const headers = [
    'Order #',
    'Date',
    'Customer',
    'Email',
    'Phone',
    'Items',
    'Total',
    'Status',
    'Payment',
    'Provider',
    'Currency',
    'Shiprocket',
  ]
  const rows = (orders || []).map((o) => {
    let ship = ''
    if (o.shiprocketShipmentCount > 0) {
      ship = `${o.shiprocketShipmentSuccessCount}/${o.shiprocketShipmentCount} pickups`
    } else if (o.shiprocketOrderId) {
      ship = String(o.shiprocketOrderId)
    } else if (o.shiprocketStatus) {
      ship = String(o.shiprocketStatus)
    }
    return [
      o.orderNumber || '',
      formatWhen(o.createdAt),
      o.customerName || '',
      o.email || '',
      o.phone || '',
      o.itemCount ?? '',
      money(o.total),
      o.status || '',
      o.paymentStatus || '',
      o.paymentProvider || '',
      o.currency || '',
      ship,
    ]
  })
  return { headers, rows }
}
