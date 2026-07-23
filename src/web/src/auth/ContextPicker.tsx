import { useEffect, useState } from 'react'
import { api, type Context, type Plant } from '../api/client'

interface Props {
  onContextSet: (context: Context) => void
}

export function ContextPicker({ onContextSet }: Props) {
  const [plants, setPlants] = useState<Plant[]>([])
  const [selected, setSelected] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    api
      .plants()
      .then((list) => {
        setPlants(list)
        if (list.length > 0) setSelected(list[0].plantId)
      })
      .catch((err) => setError(err.message))
  }, [])

  async function confirm() {
    setBusy(true)
    setError(null)
    try {
      const context = await api.setContext(selected)
      if (!context.canCreateOrders) {
        setError(
          'Tu usuario no esta asignado como comprador en esta compania. ' +
            'Puedes consultar, pero no crear ordenes. ' +
            'Pide que te agreguen en Buyer Maintenance.',
        )
      }
      onContextSet(context)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'No se pudo fijar el contexto.')
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
            <h2>Selecciona la planta</h2>
            <p className="auth-brand-sub">Define el contexto de tu sesion</p>
          </div>
        </div>

        <div className="field">
          <label htmlFor="plant" className="field-label">
            Planta
          </label>
          <select id="plant" value={selected} onChange={(e) => setSelected(e.target.value)}>
            {plants.map((plant) => (
              <option key={plant.plantId} value={plant.plantId}>
                {plant.name}
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
