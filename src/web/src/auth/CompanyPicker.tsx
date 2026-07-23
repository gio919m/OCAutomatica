import { useState } from 'react'
import { api, type CompanyOption, type Session } from '../api/client'

interface Props {
  companies: CompanyOption[]
  onCompanySelected: (session: Session) => void
}

export function CompanyPicker({ companies, onCompanySelected }: Props) {
  const [selected, setSelected] = useState(companies[0]?.company ?? '')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function confirm() {
    setBusy(true)
    setError(null)
    try {
      const session = await api.selectCompany(selected)
      onCompanySelected(session)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'No se pudo fijar la compania.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="page-center">
      <section className="auth-card">
        <div className="auth-brand">
          <span className="auth-logo">
            <img src="/logo-cfsj.jpg" alt="Carnes Finas San Juan" />
          </span>
          <div>
            <h2>Selecciona la compania</h2>
            <p className="auth-brand-sub">Tu usuario tiene acceso a mas de una</p>
          </div>
        </div>

        <div className="field">
          <label htmlFor="company" className="field-label">
            Compania
          </label>
          <select id="company" value={selected} onChange={(e) => setSelected(e.target.value)}>
            {companies.map((c) => (
              <option key={c.company} value={c.company}>
                {c.companyName}
              </option>
            ))}
          </select>
        </div>

        {error && <p role="alert">{error}</p>}

        <button
          type="button"
          onClick={confirm}
          disabled={busy || !selected}
          style={{ width: '100%', marginTop: 'var(--space-2)' }}
        >
          {busy ? 'Cargando...' : 'Continuar'}
        </button>
      </section>
    </div>
  )
}
