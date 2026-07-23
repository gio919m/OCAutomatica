import { useEffect, useState } from 'react'
import { api, type PurchaseOrderDetailLine, type PurchaseOrderSummary } from '../api/client'

interface Props {
  vendorId: string
  onBack: () => void
}

const currency = new Intl.NumberFormat('es-MX', {
  style: 'currency',
  currency: 'MXN',
  minimumFractionDigits: 2,
})

const dateFormat = new Intl.DateTimeFormat('es-MX', {
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
})

function formatDate(value: string | null): string {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '—' : dateFormat.format(date)
}

export function PurchaseOrderHistory({ vendorId, onBack }: Props) {
  const [orders, setOrders] = useState<PurchaseOrderSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [selectedPoNum, setSelectedPoNum] = useState<number | null>(null)
  const [lines, setLines] = useState<PurchaseOrderDetailLine[]>([])
  const [linesLoading, setLinesLoading] = useState(false)
  const [linesError, setLinesError] = useState<string | null>(null)
  const [sendingCopy, setSendingCopy] = useState(false)
  const [sendCopyResult, setSendCopyResult] = useState<{ ok: boolean; message: string } | null>(null)

  function loadOrders() {
    setLoading(true)
    setError(null)
    api.purchaseOrders
      .byVendor(vendorId)
      .then(setOrders)
      .catch((err) => setError(err instanceof Error ? err.message : 'Error al cargar las ordenes.'))
      .finally(() => setLoading(false))
  }

  function handleVisualizarOc() {
    if (selectedPoNum === null) return
    window.open(api.purchaseOrders.reportUrl(selectedPoNum), '_blank')
  }

  async function handleEnviarCopia() {
    if (selectedPoNum === null) return
    setSendingCopy(true)
    setSendCopyResult(null)
    try {
      const result = await api.purchaseOrders.sendCopy(selectedPoNum)
      setSendCopyResult({ ok: true, message: result.message })
    } catch (err) {
      setSendCopyResult({
        ok: false,
        message: err instanceof Error ? err.message : 'No se pudo enviar el correo.',
      })
    } finally {
      setSendingCopy(false)
    }
  }

  useEffect(() => {
    loadOrders()
    setSelectedPoNum(null)
    setLines([])
    setLinesError(null)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [vendorId])

  // Selecting an order in the grid above loads its lines below on its own —
  // matches the legacy app, which never required a separate "view" click.
  useEffect(() => {
    if (selectedPoNum === null) {
      setLines([])
      return
    }

    let cancelled = false
    setLinesLoading(true)
    setLinesError(null)
    api.purchaseOrders
      .lines(selectedPoNum)
      .then((data) => {
        if (!cancelled) setLines(data)
      })
      .catch((err) => {
        if (!cancelled) {
          setLinesError(err instanceof Error ? err.message : 'Error al cargar el detalle.')
        }
      })
      .finally(() => {
        if (!cancelled) setLinesLoading(false)
      })

    return () => {
      cancelled = true
    }
  }, [selectedPoNum])

  return (
    <section className="stack">
      <button type="button" className="btn-link" onClick={onBack} style={{ alignSelf: 'flex-start' }}>
        ← Volver a la orden
      </button>

      <div className="card-header" style={{ marginBottom: 0 }}>
        <div className="card-header-title">
          <h2>OC por proveedor</h2>
          <span className="card-header-hint">
            Ordenes de compra abiertas del ultimo ano para este proveedor
          </span>
        </div>
      </div>

      <div className="actions-row">
        <button type="button" className="btn-secondary" onClick={loadOrders}>
          Actualizar
        </button>
        <button
          type="button"
          className="btn-secondary"
          disabled={selectedPoNum === null}
          onClick={handleVisualizarOc}
        >
          Visualizar OC
        </button>
        <button
          type="button"
          className="btn-secondary"
          disabled={selectedPoNum === null || sendingCopy}
          onClick={handleEnviarCopia}
        >
          {sendingCopy ? 'Enviando...' : 'Enviar OC por email al usuario'}
        </button>
      </div>

      {sendCopyResult && (
        <p className={sendCopyResult.ok ? 'alert alert-success' : undefined} role={sendCopyResult.ok ? undefined : 'alert'}>
          {sendCopyResult.message}
        </p>
      )}

      {loading && <p className="text-muted">Cargando ordenes...</p>}
      {error && <p role="alert">{error}</p>}

      {!loading &&
        !error &&
        (orders.length === 0 ? (
          <div className="summary-empty">Este proveedor no tiene ordenes de compra registradas.</div>
        ) : (
          <div className="grid-wrap">
            <table className="data-table">
              <thead>
                <tr>
                  <th aria-label="Seleccionar" />
                  <th>Proveedor</th>
                  <th>Comprador</th>
                  <th>OC #</th>
                  <th>Fecha OC</th>
                  <th>Nombre destino</th>
                  <th>Esta abierta</th>
                  <th>OC Enviada</th>
                </tr>
              </thead>
              <tbody>
                {orders.map((o) => (
                  <tr
                    key={o.poNum}
                    className={o.poNum === selectedPoNum ? 'is-selected' : ''}
                    onClick={() => setSelectedPoNum(o.poNum)}
                  >
                    <td>
                      <input
                        type="radio"
                        checked={o.poNum === selectedPoNum}
                        onChange={() => setSelectedPoNum(o.poNum)}
                        aria-label={`Seleccionar OC ${o.poNum}`}
                      />
                    </td>
                    <td>{o.vendorName}</td>
                    <td>{o.buyerName}</td>
                    <td>{o.poNum}</td>
                    <td>{formatDate(o.orderDate)}</td>
                    <td>{o.shipName}</td>
                    <td>
                      <span className={`pill ${o.openOrder ? 'pill-strong' : ''}`}>
                        {o.openOrder ? 'Abierta' : 'Cerrada'}
                      </span>
                    </td>
                    <td className="text-muted">Pendiente</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ))}

      <div className="card-header" style={{ marginBottom: 0 }}>
        <div className="card-header-title">
          <h2>Detalle de orden de compra</h2>
          <span className="card-header-hint">
            {selectedPoNum === null ? 'Selecciona una orden en la tabla de arriba' : `OC ${selectedPoNum}`}
          </span>
        </div>
      </div>

      {linesLoading && <p className="text-muted">Cargando detalle...</p>}
      {linesError && <p role="alert">{linesError}</p>}

      {!linesLoading && !linesError && lines.length > 0 && (
        <div className="grid-wrap">
          <table className="data-table">
            <thead>
              <tr>
                <th>Linea</th>
                <th>REL</th>
                <th>Codigo producto</th>
                <th>Descripcion del producto</th>
                <th>Cantidad OC</th>
                <th>UM</th>
                <th>Costo unitario</th>
                <th>Recibido</th>
                <th>Pendiente</th>
                <th>Total</th>
              </tr>
            </thead>
            <tbody>
              {lines.map((l) => (
                <tr key={`${l.line}-${l.rel}`}>
                  <td>{l.line}</td>
                  <td>{l.rel}</td>
                  <td>{l.partNum}</td>
                  <td>{l.description}</td>
                  <td>{l.orderQty}</td>
                  <td>{l.uom}</td>
                  <td>{currency.format(l.unitCost)}</td>
                  <td>{l.receivedQty}</td>
                  <td>{l.pendingQty}</td>
                  <td>{currency.format(l.total)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}
