import { useState, type FormEvent } from 'react'
import { api, type Session } from '../api/client'

interface Props {
  onSignedIn: (session: Session) => void
  sessionExpired?: boolean
}

export function LoginPage({ onSignedIn, sessionExpired }: Props) {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    setBusy(true)
    try {
      const session = await api.login(username, password)
      onSignedIn(session)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'No se pudo iniciar sesion.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="page-center">
      <form onSubmit={handleSubmit} className="auth-card">
        <div className="auth-brand">
          <span className="auth-logo">
            <img src="/logo-cfsj.jpg" alt="Carnes Finas San Juan" />
          </span>
          <div>
            <h1>OC Automatica</h1>
            <p className="auth-brand-sub">Sistema de ordenes de compra</p>
          </div>
        </div>

        <div className="field">
          <label htmlFor="username" className="field-label">
            Usuario
          </label>
          <input
            id="username"
            value={username}
            onChange={(e) => setUsername(e.target.value)}
            autoComplete="username"
            required
          />
        </div>

        <div className="field">
          <label htmlFor="password" className="field-label">
            Contrasena
          </label>
          <input
            id="password"
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            autoComplete="current-password"
            required
          />
        </div>

        {sessionExpired && !error && (
          <p role="alert">Tu sesion expiro. Vuelve a iniciar sesion.</p>
        )}
        {error && <p role="alert">{error}</p>}

        <button type="submit" disabled={busy} style={{ width: '100%', marginTop: 'var(--space-2)' }}>
          {busy ? 'Validando...' : 'Entrar'}
        </button>
      </form>
    </div>
  )
}
