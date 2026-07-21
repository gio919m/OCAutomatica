import { useEffect, useState, type FormEvent } from 'react'
import { api, type Session } from '../api/client'

interface Props {
  onSignedIn: (session: Session) => void
}

export function LoginPage({ onSignedIn }: Props) {
  const [companies, setCompanies] = useState<string[]>([])
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [company, setCompany] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    api
      .companies()
      .then((list) => {
        setCompanies(list)
        if (list.length > 0) setCompany(list[0])
      })
      .catch(() => setError('No se pudo cargar la lista de companias.'))
  }, [])

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    setBusy(true)
    try {
      const session = await api.login(username, password, company)
      onSignedIn(session)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'No se pudo iniciar sesion.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={handleSubmit}>
      <h1>OC Automatica</h1>

      <label htmlFor="company">Compania</label>
      <select
        id="company"
        value={company}
        onChange={(e) => setCompany(e.target.value)}
      >
        {companies.map((c) => (
          <option key={c} value={c}>
            {c}
          </option>
        ))}
      </select>

      <label htmlFor="username">Usuario</label>
      <input
        id="username"
        value={username}
        onChange={(e) => setUsername(e.target.value)}
        autoComplete="username"
        required
      />

      <label htmlFor="password">Contrasena</label>
      <input
        id="password"
        type="password"
        value={password}
        onChange={(e) => setPassword(e.target.value)}
        autoComplete="current-password"
        required
      />

      {error && <p role="alert">{error}</p>}

      <button type="submit" disabled={busy || !company}>
        {busy ? 'Validando...' : 'Entrar'}
      </button>
    </form>
  )
}
