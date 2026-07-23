import { useEffect, useRef, useState } from 'react'
import { api, type Vendor } from '../api/client'

interface Props {
  onVendorSelected: (vendor: Vendor) => void
}

export function VendorSearch({ onVendorSelected }: Props) {
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<Vendor[]>([])
  const [selected, setSelected] = useState<Vendor | null>(null)
  const [error, setError] = useState<string | null>(null)
  const latestQueryRef = useRef('')

  useEffect(() => {
    if (query.trim().length < 2 || selected) {
      setResults([])
      setError(null)
      return
    }

    const trimmedQuery = query.trim()

    const handle = setTimeout(() => {
      setError(null)
      latestQueryRef.current = trimmedQuery
      api
        .vendors.search(trimmedQuery)
        .then((data) => {
          if (latestQueryRef.current === trimmedQuery) setResults(data)
        })
        .catch((err) => {
          if (latestQueryRef.current === trimmedQuery) {
            setError(err instanceof Error ? err.message : 'Error de busqueda.')
          }
        })
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
    <div className="field search-box" style={{ marginBottom: 0 }}>
      <label htmlFor="vendor-search" className="field-label">
        Proveedor
      </label>
      <input
        id="vendor-search"
        value={query}
        onChange={(e) => handleChange(e.target.value)}
        placeholder="Buscar por nombre o codigo..."
        autoComplete="off"
      />

      {error && <p role="alert">{error}</p>}

      {results.length > 0 && (
        <ul className="search-results">
          {results.map((vendor) => (
            <li key={vendor.vendorId} className="search-result-item">
              <button type="button" onClick={() => pick(vendor)}>
                <span className="search-result-code">{vendor.vendorId}</span>
                <span>{vendor.name}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
