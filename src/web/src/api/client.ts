export interface Session {
  username: string
  company: string
  plant: string
  buyerId: string | null
  buyerName: string | null
}

export interface Plant {
  plantId: string
  name: string
}

export interface Context {
  company: string
  plant: string
  buyerId: string | null
  buyerName: string | null
  canCreateOrders: boolean
}

export class ApiError extends Error {
  status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    credentials: 'include',
    headers: { 'Content-Type': 'application/json', ...init?.headers },
  })

  if (!response.ok) {
    let message = 'Ocurrio un error inesperado.'
    try {
      const body = await response.json()
      if (body?.message) message = body.message
    } catch {
      // Response body was not JSON; fall back to the generic message.
    }
    throw new ApiError(response.status, message)
  }

  return response.status === 204 ? (undefined as T) : response.json()
}

export const api = {
  companies: () => request<string[]>('/api/auth/companies'),

  login: (username: string, password: string, company: string) =>
    request<Session>('/api/auth/login', {
      method: 'POST',
      body: JSON.stringify({ username, password, company }),
    }),

  logout: () => request<void>('/api/auth/logout', { method: 'POST' }),

  me: () => request<Session>('/api/auth/me'),

  plants: () => request<Plant[]>('/api/organization/plants'),

  setContext: (plant: string) =>
    request<Context>('/api/organization/context', {
      method: 'POST',
      body: JSON.stringify({ plant }),
    }),
}
