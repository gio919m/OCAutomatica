import { useState } from 'react'
import { type Context, type Vendor } from './api/client'
import { LoginPage } from './auth/LoginPage'
import { ContextPicker } from './auth/ContextPicker'
import { useSession } from './auth/useSession'
import { VendorSearch } from './vendors/VendorSearch'
import { PartsGrid } from './parts/PartsGrid'

export default function App() {
  const { session, setSession, loading, signOut } = useSession()
  const [context, setContext] = useState<Context | null>(null)
  const [vendor, setVendor] = useState<Vendor | null>(null)

  if (loading) return <p>Cargando...</p>

  if (!session) return <LoginPage onSignedIn={setSession} />

  if (!context) return <ContextPicker onContextSet={setContext} />

  return (
    <main>
      <header>
        <span>{session.username}</span>
        <span>
          {context.company} / {context.plant}
        </span>
        <span>{context.buyerName ?? 'Sin comprador asignado'}</span>
        <button type="button" onClick={signOut}>
          Salir
        </button>
      </header>

      <VendorSearch onVendorSelected={setVendor} />

      {vendor && <PartsGrid key={vendor.vendorId} vendorId={vendor.vendorId} />}
    </main>
  )
}
