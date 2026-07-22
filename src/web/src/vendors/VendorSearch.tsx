import { useEffect, useState } from 'react'
import { api, type Vendor } from '../api/client'

interface Props {
  onVendorSelected: (vendor: Vendor) => void
}

export function VendorSearch({ onVendorSelected }: Props) {
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<Vendor[]>([])
  const [selected, setSelected] = useState<Vendor | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (query.trim().length < 2 || selected) {
      setResults([])
      return
    }

    const handle = setTimeout(() => {
      api
        .vendors.search(query)
        .then(setResults)
        .catch((err) => setError(err instanceof Error ? err.message : 'Error de busqueda.'))
    }, 300)

    return () => clearTimeout(handle)
  }, [query, selected])

  function pick(vendor: Vendor) {
    setSelected(vendor)
    setQuery(vendor.name)
    setResults([])
    onVendorSelected(vendor)
  }

  function handleChange(value: string) {
    setQuery(value)
    if (selected) setSelected(null)
  }

  return (
    <div>
      <label htmlFor="vendor-search">Proveedor</label>
      <input
        id="vendor-search"
        value={query}
        onChange={(e) => handleChange(e.target.value)}
        placeholder="Buscar por nombre o codigo..."
        autoComplete="off"
      />

      {error && <p role="alert">{error}</p>}

      {results.length > 0 && (
        <ul>
          {results.map((vendor) => (
            <li key={vendor.vendorId}>
              <button type="button" onClick={() => pick(vendor)}>
                {vendor.vendorId} — {vendor.name}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
