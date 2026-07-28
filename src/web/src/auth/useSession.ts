import { useCallback, useEffect, useState } from 'react'
import { api, type Session } from '../api/client'

export function useSession() {
  const [session, setSession] = useState<Session | null>(null)
  const [loading, setLoading] = useState(true)
  const [ssoSite, setSsoSite] = useState<string | null>(null)

  useEffect(() => {
    const params = new URLSearchParams(window.location.search)
    const token = params.get('token')

    if (token) {
      const company = params.get('company') ?? undefined
      const site = params.get('site') ?? undefined
      // Clean the URL immediately so a page refresh never re-sends an
      // already-used (and by then likely expired) token.
      window.history.replaceState({}, '', window.location.pathname)

      api
        .ssoLogin(token, company, site)
        .then((ssoSession) => {
          setSession(ssoSession)
          if (site) setSsoSite(site)
        })
        .catch(() => setSession(null))
        .finally(() => setLoading(false))
      return
    }

    api
      .me()
      .then(setSession)
      .catch(() => setSession(null))
      .finally(() => setLoading(false))
  }, [])

  const signOut = useCallback(async () => {
    await api.logout()
    setSession(null)
    setSsoSite(null)
  }, [])

  return { session, setSession, loading, signOut, ssoSite }
}
