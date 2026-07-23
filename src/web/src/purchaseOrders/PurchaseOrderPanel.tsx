import { useEffect, useRef, useState } from 'react'
import { api, type CambioFisico } from '../api/client'
import { isRowInvalid, type PartRowState } from '../parts/PartsGrid'
import { Modal } from '../components/Modal'

interface Props {
  vendorId: string
  rows: PartRowState[]
  canCreateOrders: boolean
}

const currency = new Intl.NumberFormat('es-MX', {
  style: 'currency',
  currency: 'MXN',
  minimumFractionDigits: 2,
})

export function PurchaseOrderPanel({ vendorId, rows, canCreateOrders }: Props) {
  const [cambiosFisicos, setCambiosFisicos] = useState<CambioFisico[]>([])
  const [comentarios, setComentarios] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [poNum, setPoNum] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [comentariosDraft, setComentariosDraft] = useState<string | null>(null)
  const latestVendorIdRef = useRef(vendorId)

  useEffect(() => {
    latestVendorIdRef.current = vendorId
    setCambiosFisicos([])
    setComentarios('')
    setPoNum(null)
    setError(null)
    api.cambiosFisicos
      .byVendor(vendorId)
      .then((data) => {
        if (latestVendorIdRef.current === vendorId) setCambiosFisicos(data)
      })
      .catch(() => {
        if (latestVendorIdRef.current === vendorId) setCambiosFisicos([])
      })
  }, [vendorId])

  const selectedLines = rows.filter((r) => r.assign && !isRowInvalid(r))
  const hasInvalidSelection = rows.some((r) => r.assign && isRowInvalid(r))
  const canProcess =
    canCreateOrders &&
    selectedLines.length > 0 &&
    !hasInvalidSelection &&
    !submitting &&
    poNum === null

  const totalQty = selectedLines.reduce((sum, r) => sum + (r.qtyToFill as number), 0)
  const totalAmount = selectedLines.reduce(
    (sum, r) => sum + (r.qtyToFill as number) * r.cost,
    0,
  )

  async function handleProcesar() {
    const submittedVendorId = vendorId
    setSubmitting(true)
    setError(null)
    setPoNum(null)
    try {
      const lineas = selectedLines.map((r) => ({
        partNum: r.partNum,
        cantidad: r.qtyToFill as number,
        costo: r.cost,
        uom: r.uom,
      }))
      const result = await api.purchaseOrders.create(vendorId, comentarios, lineas)
      if (latestVendorIdRef.current === submittedVendorId) setPoNum(result.poNum)
    } catch (err) {
      if (latestVendorIdRef.current === submittedVendorId) {
        setError(err instanceof Error ? err.message : 'No se pudo crear la orden de compra.')
      }
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <section className="summary">
      <div className="summary-head">
        <span className="summary-title">Resumen de orden</span>
        <span className="summary-count">{selectedLines.length}</span>
      </div>

      <div className="summary-body">
        <div className="summary-stats">
          <div className="summary-stat">
            <span className="summary-stat-label">Articulos seleccionados</span>
            <span className="summary-stat-value">{selectedLines.length}</span>
          </div>
          <div className="summary-stat">
            <span className="summary-stat-label">Cantidad total</span>
            <span className="summary-stat-value">{totalQty.toLocaleString('es-MX')}</span>
          </div>
          <div className="summary-stat summary-total">
            <span className="summary-stat-label">Total estimado</span>
            <span className="summary-stat-value">{currency.format(totalAmount)}</span>
          </div>
        </div>

        {selectedLines.length > 0 ? (
          <div>
            <span className="summary-lines-label">Articulos</span>
            <div className="summary-lines">
              {selectedLines.map((r) => (
                <div key={r.partNum} className="summary-line">
                  <div className="summary-line-top">
                    <span className="summary-line-part">{r.partNum}</span>
                    <span className="summary-line-amount">
                      {currency.format((r.qtyToFill as number) * r.cost)}
                    </span>
                  </div>
                  <span className="summary-line-desc">{r.partDescription}</span>
                  <span className="summary-line-qty">
                    {r.qtyToFill} {r.uom} × {currency.format(r.cost)}
                  </span>
                </div>
              ))}
            </div>
          </div>
        ) : (
          <div className="summary-empty">
            Marca articulos en la tabla para armar la orden.
          </div>
        )}

        {cambiosFisicos.length > 0 && (
          <div className="callout">
            <span className="callout-label">Cambios fisicos pendientes</span>
            {cambiosFisicos
              .map(
                (c) =>
                  `${c.character01}  ${c.character02}  ${c.character04}  ${c.cantidadPendiente.toFixed(2)}  ${c.character06}`,
              )
              .join(' - ')}
          </div>
        )}

        <div className="field" style={{ marginBottom: 0 }}>
          <div className="field-label-row">
            <label htmlFor="comentarios" className="field-label">
              Comentarios adicionales
            </label>
            <button
              type="button"
              className="btn-link"
              onClick={() => setComentariosDraft(comentarios)}
            >
              Maximizar
            </button>
          </div>
          <textarea
            id="comentarios"
            value={comentarios}
            onChange={(e) => setComentarios(e.target.value)}
            placeholder="Notas para el proveedor o para corporativo..."
          />
        </div>

        {comentariosDraft !== null && (
          <Modal
            title="Ingrese el comentario"
            onClose={() => setComentariosDraft(null)}
            footer={
              <>
                <button
                  type="button"
                  className="btn-secondary"
                  onClick={() => setComentariosDraft(null)}
                >
                  Cancelar
                </button>
                <button
                  type="button"
                  onClick={() => {
                    setComentarios(comentariosDraft)
                    setComentariosDraft(null)
                  }}
                >
                  Aceptar
                </button>
              </>
            }
          >
            <textarea
              value={comentariosDraft}
              onChange={(e) => setComentariosDraft(e.target.value)}
              placeholder="Notas para el proveedor o para corporativo..."
            />
          </Modal>
        )}

        {!canCreateOrders && (
          <p role="alert">
            No tienes un comprador asignado en esta compania. No puedes crear ordenes de compra.
          </p>
        )}
        {hasInvalidSelection && (
          <p role="alert">
            Hay articulos marcados con cantidad o costo invalido. Corrigelos antes de procesar.
          </p>
        )}
        {error && <p role="alert">{error}</p>}
        {poNum !== null && (
          <p className="alert alert-success">Orden de compra creada: OC {poNum}</p>
        )}

        <button
          type="button"
          className="btn-block"
          disabled={!canProcess}
          onClick={handleProcesar}
        >
          {submitting ? 'Procesando...' : 'Procesar orden de compra'}
        </button>
      </div>
    </section>
  )
}
