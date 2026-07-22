import { useEffect, useMemo, useState } from 'react'
import { AgGridReact } from 'ag-grid-react'
import type { ColDef, ValueGetterParams, ValueSetterParams } from 'ag-grid-community'
import { AllCommunityModule, ModuleRegistry } from 'ag-grid-community'
import { api, type PartRow } from '../api/client'
import { SelectionCounter } from './SelectionCounter'

ModuleRegistry.registerModules([AllCommunityModule])

export interface PartRowState extends PartRow {
  assign: boolean
  qtyToFill: number | null
}

interface Props {
  vendorId: string
}

function isRowInvalid(row: PartRowState): boolean {
  if (!row.assign) return false
  return row.qtyToFill === null || row.qtyToFill <= 0 || row.cost <= 0
}

export function PartsGrid({ vendorId }: Props) {
  const [rows, setRows] = useState<PartRowState[]>([])
  const [quickFilter, setQuickFilter] = useState('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    setLoading(true)
    setError(null)
    api
      .parts.byVendor(vendorId)
      .then((parts) =>
        setRows(parts.map((p) => ({ ...p, assign: false, qtyToFill: null }))),
      )
      .catch((err) => setError(err instanceof Error ? err.message : 'Error al cargar productos.'))
      .finally(() => setLoading(false))
  }, [vendorId])

  const selectedCount = useMemo(() => rows.filter((r) => r.assign).length, [rows])

  function updateRow(partNum: string, patch: Partial<PartRowState>) {
    setRows((prev) =>
      prev.map((row) => (row.partNum === partNum ? { ...row, ...patch } : row)),
    )
  }

  const columnDefs = useMemo<ColDef<PartRowState>[]>(
    () => [
      {
        field: 'assign',
        headerName: 'Asignar',
        editable: true,
        cellDataType: 'boolean',
        onCellValueChanged: (e) => updateRow(e.data!.partNum, { assign: e.newValue }),
      },
      { field: 'partNum', headerName: 'No. Parte', editable: false },
      { field: 'partDescription', headerName: 'Descripcion', editable: false, flex: 1 },
      { field: 'ean13', headerName: 'EAN', editable: false },
      { field: 'minimumQty', headerName: 'Minimo', editable: false },
      { field: 'maximumQty', headerName: 'Maximos', editable: false },
      { field: 'onHandQty', headerName: 'Inventario', editable: false },
      { field: 'cost', headerName: 'Costo', editable: false },
      {
        field: 'qtyToFill',
        headerName: 'Cantidad a Surtir',
        editable: (params) => params.data?.assign === true,
        valueGetter: (params: ValueGetterParams<PartRowState>) => params.data?.qtyToFill,
        valueSetter: (params: ValueSetterParams<PartRowState>) => {
          const parsed = params.newValue === '' ? null : Number(params.newValue)
          const value = parsed === null || Number.isNaN(parsed) ? null : parsed
          updateRow(params.data!.partNum, { qtyToFill: value })
          return true
        },
        cellClassRules: {
          'cell-invalid': (params) => isRowInvalid(params.data as PartRowState),
        },
      },
      { field: 'inTransitQty', headerName: 'Cantidad En Transito', editable: false },
      { field: 'uom', headerName: 'UM', editable: false },
    ],
    [],
  )

  if (loading) return <p>Cargando productos...</p>
  if (error) return <p role="alert">{error}</p>

  return (
    <section>
      <label htmlFor="quick-filter">Buscar</label>
      <input
        id="quick-filter"
        value={quickFilter}
        onChange={(e) => setQuickFilter(e.target.value)}
        placeholder="Filtrar por descripcion, parte..."
      />

      <SelectionCounter count={selectedCount} />

      <div style={{ height: 500 }}>
        <AgGridReact<PartRowState>
          rowData={rows}
          columnDefs={columnDefs}
          quickFilterText={quickFilter}
          getRowId={(params) => params.data.partNum}
        />
      </div>
    </section>
  )
}
