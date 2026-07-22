import { useEffect, useRef, useState } from 'react'
import { api, type CambioFisico } from '../api/client'
import { isRowInvalid, type PartRowState } from '../parts/PartsGrid'

interface Props {
  vendorId: string
  rows: PartRowState[]
  canCreateOrders: boolean
}

export function PurchaseOrderPanel({ vendorId, rows, canCreateOrders }: Props) {
  const [cambiosFisicos, setCambiosFisicos] = useState<CambioFisico[]>([])
  const [comentarios, setComentarios] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [poNum, setPoNum] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)
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
    <section>
      <h2>Comentarios</h2>

      {cambiosFisicos.length > 0 && (
        <p>
          Cambios Fisicos pendientes:{' '}
          {cambiosFisicos
            .map(
              (c) =>
                `${c.character01}  ${c.character02}  ${c.character04}  ${c.cantidadPendiente.toFixed(2)}  ${c.character06}`,
            )
            .join(' - ')}
        </p>
      )}

      <label htmlFor="comentarios">Comentarios adicionales</label>
      <textarea
        id="comentarios"
        value={comentarios}
        onChange={(e) => setComentarios(e.target.value)}
      />

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
      {poNum !== null && <p>Orden de compra creada: OC {poNum}</p>}

      <button type="button" disabled={!canProcess} onClick={handleProcesar}>
        {submitting ? 'Procesando...' : 'Procesar'}
      </button>
    </section>
  )
}
