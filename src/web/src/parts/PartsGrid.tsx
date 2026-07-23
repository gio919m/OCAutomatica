import { useEffect, useMemo, useRef, useState } from 'react'
import { AgGridReact } from 'ag-grid-react'
import type { ColDef, ValueFormatterParams, ValueGetterParams, ValueSetterParams } from 'ag-grid-community'
import { AllCommunityModule, ModuleRegistry, themeQuartz } from 'ag-grid-community'
import { api, type PartRow } from '../api/client'
import { SelectionCounter } from './SelectionCounter'

ModuleRegistry.registerModules([AllCommunityModule])

const gridTheme = themeQuartz.withParams({
  accentColor: '#d81f2a',
  headerBackgroundColor: '#f6f7f9',
  headerTextColor: '#191c24',
  headerFontWeight: 600,
  borderColor: '#e3e5ec',
  oddRowBackgroundColor: '#fafbfc',
  rowHoverColor: '#fdeced',
  fontFamily: "'Segoe UI', system-ui, sans-serif",
  fontSize: 13,
  spacing: 6,
})

const currency = new Intl.NumberFormat('es-MX', {
  style: 'currency',
  currency: 'MXN',
  minimumFractionDigits: 2,
})

export interface PartRowState extends PartRow {
  assign: boolean
  qtyToFill: number | null
}

interface Props {
  vendorId: string
  onRowsChange?: (rows: PartRowState[]) => void
}

export function isRowInvalid(row: PartRowState): boolean {
  if (!row.assign) return false
  return row.qtyToFill === null || row.qtyToFill <= 0 || row.cost <= 0
}

export function PartsGrid({ vendorId, onRowsChange }: Props) {
  const gridRef = useRef<AgGridReact<PartRowState>>(null)
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

  useEffect(() => {
    onRowsChange?.(rows)
  }, [rows, onRowsChange])

  function updateRow(partNum: string, patch: Partial<PartRowState>) {
    setRows((prev) =>
      prev.map((row) => (row.partNum === partNum ? { ...row, ...patch } : row)),
    )
  }

  const columnDefs = useMemo<ColDef<PartRowState>[]>(
    () => [
      {
        field: 'assign',
        headerName: 'Surtir',
        pinned: 'left',
        width: 82,
        editable: true,
        cellDataType: 'boolean',
        sortable: false,
        headerTooltip: 'Marca los articulos que vas a surtir',
        onCellValueChanged: (e) => {
          updateRow(e.data!.partNum, { assign: e.newValue })
          if (e.newValue === true && e.node?.rowIndex != null) {
            const rowIndex = e.node.rowIndex
            // Al marcar, salta directo a capturar la cantidad.
            setTimeout(() => {
              gridRef.current?.api.startEditingCell({ rowIndex, colKey: 'qtyToFill' })
            }, 0)
          }
        },
      },
      { field: 'partNum', headerName: 'No. Parte', pinned: 'left', width: 120, editable: false },
      {
        field: 'partDescription',
        headerName: 'Descripcion',
        editable: false,
        flex: 1,
        minWidth: 260,
        tooltipField: 'partDescription',
      },
      { field: 'uom', headerName: 'UM', width: 74, editable: false },
      { field: 'onHandQty', headerName: 'Inventario', type: 'numericColumn', width: 110, editable: false },
      { field: 'inTransitQty', headerName: 'En transito', type: 'numericColumn', width: 118, editable: false },
      { field: 'minimumQty', headerName: 'Min', type: 'numericColumn', width: 84, editable: false },
      { field: 'maximumQty', headerName: 'Max', type: 'numericColumn', width: 84, editable: false },
      {
        field: 'cost',
        headerName: 'Costo',
        type: 'numericColumn',
        width: 110,
        editable: false,
        valueFormatter: (p: ValueFormatterParams<PartRowState, number>) =>
          p.value != null ? currency.format(p.value) : '',
      },
      { field: 'ean13', headerName: 'EAN-13', width: 132, editable: false },
      { field: 'ean14', headerName: 'EAN-14', width: 132, editable: false },
      {
        field: 'qtyToFill',
        headerName: 'Cantidad a surtir',
        pinned: 'right',
        width: 152,
        type: 'numericColumn',
        editable: (params) => params.data?.assign === true,
        singleClickEdit: true,
        valueGetter: (params: ValueGetterParams<PartRowState>) => params.data?.qtyToFill,
        valueSetter: (params: ValueSetterParams<PartRowState>) => {
          const parsed = params.newValue === '' ? null : Number(params.newValue)
          const value = parsed === null || Number.isNaN(parsed) ? null : parsed
          updateRow(params.data!.partNum, { qtyToFill: value })
          return true
        },
        cellClassRules: {
          'cell-qty': (params) => (params.data as PartRowState)?.assign === true,
          'cell-invalid': (params) => isRowInvalid(params.data as PartRowState),
        },
      },
    ],
    [],
  )

  const defaultColDef = useMemo<ColDef<PartRowState>>(
    () => ({ resizable: true, sortable: true }),
    [],
  )

  if (loading) return <p className="text-muted">Cargando productos...</p>
  if (error) return <p role="alert">{error}</p>

  return (
    <section>
      <div className="card-header">
        <div className="card-header-title">
          <h2>Productos del proveedor</h2>
          <span className="card-header-hint">
            Marca los articulos a surtir y captura la cantidad
          </span>
        </div>
        <SelectionCounter count={selectedCount} />
      </div>

      <div className="grid-toolbar">
        <div className="field">
          <label htmlFor="quick-filter" className="field-label">
            Buscar
          </label>
          <input
            id="quick-filter"
            value={quickFilter}
            onChange={(e) => setQuickFilter(e.target.value)}
            placeholder="Filtrar por descripcion, parte, EAN..."
          />
        </div>
      </div>

      <div className="grid-wrap" style={{ height: 520 }}>
        <AgGridReact<PartRowState>
          ref={gridRef}
          theme={gridTheme}
          rowData={rows}
          columnDefs={columnDefs}
          defaultColDef={defaultColDef}
          quickFilterText={quickFilter}
          getRowId={(params) => params.data.partNum}
          rowHeight={42}
          headerHeight={42}
          animateRows
          stopEditingWhenCellsLoseFocus
        />
      </div>
    </section>
  )
}
