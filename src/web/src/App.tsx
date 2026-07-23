import { useState } from 'react'
import { type Context, type Vendor } from './api/client'
import { LoginPage } from './auth/LoginPage'
import { CompanyPicker } from './auth/CompanyPicker'
import { ContextPicker } from './auth/ContextPicker'
import { useSession } from './auth/useSession'
import { VendorSearch } from './vendors/VendorSearch'
import { PartsGrid, type PartRowState } from './parts/PartsGrid'
import { PurchaseOrderPanel } from './purchaseOrders/PurchaseOrderPanel'
import { PurchaseOrderHistory } from './purchaseOrders/PurchaseOrderHistory'

type StepStatus = 'done' | 'active' | 'pending'

function Step({
  status,
  index,
  title,
  sub,
}: {
  status: StepStatus
  index: number
  title: string
  sub: string
}) {
  return (
    <div className={`step is-${status}`}>
      <span className="step-marker">{status === 'done' ? '✓' : index}</span>
      <span className="step-body">
        <span className="step-title">{title}</span>
        {sub && <span className="step-sub">{sub}</span>}
      </span>
    </div>
  )
}

export default function App() {
  const { session, setSession, loading, signOut } = useSession()
  const [context, setContext] = useState<Context | null>(null)
  const [vendor, setVendor] = useState<Vendor | null>(null)
  const [rows, setRows] = useState<PartRowState[]>([])
  const [activeTab, setActiveTab] = useState<'nueva' | 'historial'>('nueva')

  if (loading) return <div className="page-loading">Cargando...</div>

  async function handleSignOut() {
    await signOut()
    setContext(null)
    setVendor(null)
    setRows([])
  }

  if (!session) return <LoginPage onSignedIn={setSession} />

  if (!session.company) {
    return <CompanyPicker companies={session.companies} onCompanySelected={setSession} />
  }

  if (!context) return <ContextPicker onContextSet={setContext} />

  function handleVendorSelected(selected: Vendor) {
    setVendor(selected)
    setRows([])
    setActiveTab('nueva')
  }

  const selectedCount = rows.filter((r) => r.assign).length
  const initials = session.username.slice(0, 2).toUpperCase()
  const companyName =
    session.companies.find((c) => c.company === context.company)?.companyName ?? context.company

  const proveedorStatus: StepStatus = vendor ? 'done' : 'active'
  const productosStatus: StepStatus = !vendor
    ? 'pending'
    : selectedCount > 0
      ? 'done'
      : 'active'
  const procesarStatus: StepStatus = selectedCount > 0 ? 'active' : 'pending'

  return (
    <div className="layout">
      <aside className="sidebar">
        <div className="sidebar-logo">
          <img src="/logo-cfsj.jpg" alt="Carnes Finas San Juan" />
        </div>

        <div>
          <div className="sidebar-section">Flujo de la orden</div>
          <nav className="sidebar-nav">
            <Step
              status={proveedorStatus}
              index={1}
              title="Proveedor"
              sub={vendor ? vendor.name : 'Busca y selecciona'}
            />
            <Step
              status={productosStatus}
              index={2}
              title="Productos"
              sub={vendor ? `${selectedCount} seleccionado(s)` : 'Pendiente'}
            />
            <Step
              status={procesarStatus}
              index={3}
              title="Procesar"
              sub={selectedCount > 0 ? 'Listo para revisar' : 'Pendiente'}
            />
          </nav>
        </div>

        {vendor && (
          <div>
            <div className="sidebar-section">Vista</div>
            <nav className="sidebar-nav">
              <button
                type="button"
                className={`sidebar-tab ${activeTab === 'historial' ? 'is-active' : ''}`}
                onClick={() => setActiveTab(activeTab === 'historial' ? 'nueva' : 'historial')}
              >
                {activeTab === 'historial' ? '← Volver a la orden' : 'OCs por proveedor'}
              </button>
            </nav>
          </div>
        )}

        <div className="sidebar-foot">
          <div className="user-chip">
            <span className="user-avatar">{initials}</span>
            <span className="user-meta">
              <span className="user-name">{session.username}</span>
              <span className="user-role">
                {context.canCreateOrders ? 'Comprador' : 'Solo consulta'}
              </span>
            </span>
          </div>
          <button type="button" className="sidebar-logout" onClick={handleSignOut}>
            Cerrar sesion
          </button>
        </div>
      </aside>

      <div className="workspace">
        <header className="topbar">
          <div className="topbar-head">
            <span className="topbar-title">
              {activeTab === 'nueva' ? 'Nueva orden de compra' : 'OCs por proveedor'}
            </span>
            <span className="topbar-sub">
              {vendor
                ? `Proveedor ${vendor.vendorId} · ${vendor.name}`
                : 'Selecciona un proveedor para comenzar'}
            </span>
          </div>
          <div className="topbar-meta">
            <span className="pill pill-strong">
              {companyName} &middot; {context.plant}
            </span>
            <span className="pill">
              <span
                className={`pill-dot ${context.canCreateOrders ? 'pill-dot-active' : ''}`}
              />
              {context.buyerName ?? 'Sin comprador asignado'}
            </span>
          </div>
        </header>

        <div className="workspace-body">
          {vendor ? (
            activeTab === 'nueva' ? (
              <div className="work-grid">
                <div className="work-main">
                  <div className="card">
                    <VendorSearch onVendorSelected={handleVendorSelected} />
                  </div>
                  <div className="card">
                    <PartsGrid
                      key={vendor.vendorId}
                      vendorId={vendor.vendorId}
                      onRowsChange={setRows}
                    />
                  </div>
                </div>
                <aside className="work-aside">
                  <PurchaseOrderPanel
                    key={vendor.vendorId}
                    vendorId={vendor.vendorId}
                    rows={rows}
                    canCreateOrders={context.canCreateOrders}
                  />
                </aside>
              </div>
            ) : (
              <div className="card">
                <PurchaseOrderHistory
                  key={vendor.vendorId}
                  vendorId={vendor.vendorId}
                  onBack={() => setActiveTab('nueva')}
                />
              </div>
            )
          ) : (
            <div className="work-single">
              <div className="card">
                <VendorSearch onVendorSelected={handleVendorSelected} />
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
