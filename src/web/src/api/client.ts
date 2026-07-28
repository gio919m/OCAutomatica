export interface CompanyOption {
  company: string
  companyName: string
}

export interface Session {
  username: string
  company: string
  plant: string
  buyerId: string | null
  buyerName: string | null
  companies: CompanyOption[]
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

export interface Vendor {
  vendorId: string
  name: string
}

export interface PartRow {
  vendorId: string
  vendorName: string
  partNum: string
  partDescription: string
  uom: string
  minimumQty: number
  maximumQty: number
  cost: number
  ean13: string
  ean14: string
  onHandQty: number
  inTransitQty: number
}

export interface PurchaseOrderLine {
  partNum: string
  cantidad: number
  costo: number
  uom: string
}

export interface CreatePurchaseOrderResult {
  poNum: number
}

export interface PurchaseOrderSummary {
  poNum: number
  vendorId: string
  vendorName: string
  buyerId: string
  buyerName: string
  orderDate: string | null
  shipName: string
  openOrder: boolean
}

export interface PurchaseOrderDetailLine {
  line: number
  rel: number
  partNum: string
  description: string
  orderQty: number
  uom: string
  unitCost: number
  receivedQty: number
  pendingQty: number
  total: number
}

export interface CambioFisico {
  character01: string
  character02: string
  character04: string
  cantidadPendiente: number
  character06: string
}

export interface EmailRecipient {
  tipo: string
  email: string
  valido: boolean
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
  login: (username: string, password: string) =>
    request<Session>('/api/auth/login', {
      method: 'POST',
      body: JSON.stringify({ username, password }),
    }),

  selectCompany: (company: string) =>
    request<Session>('/api/auth/company', {
      method: 'POST',
      body: JSON.stringify({ company }),
    }),

  logout: () => request<void>('/api/auth/logout', { method: 'POST' }),

  me: () => request<Session>('/api/auth/me'),

  ssoLogin: (token: string, company?: string, site?: string) =>
    request<Session>('/api/auth/sso-login', {
      method: 'POST',
      body: JSON.stringify({ token, company: company ?? null, site: site ?? null }),
    }),

  plants: () => request<Plant[]>('/api/organization/plants'),

  setContext: (plant: string) =>
    request<Context>('/api/organization/context', {
      method: 'POST',
      body: JSON.stringify({ plant }),
    }),

  vendors: {
    search: (query: string) =>
      request<Vendor[]>(`/api/vendors?search=${encodeURIComponent(query)}`),
  },

  parts: {
    byVendor: (vendorId: string) =>
      request<PartRow[]>(`/api/parts?vendorId=${encodeURIComponent(vendorId)}`),
  },

  cambiosFisicos: {
    byVendor: (vendorId: string) =>
      request<CambioFisico[]>(`/api/cambios-fisicos?vendorId=${encodeURIComponent(vendorId)}`),
  },

  purchaseOrders: {
    create: (vendorId: string, comentarios: string, lineas: PurchaseOrderLine[]) =>
      request<CreatePurchaseOrderResult>('/api/purchase-orders', {
        method: 'POST',
        body: JSON.stringify({ vendorId, comentarios, lineas }),
      }),

    byVendor: (vendorId: string) =>
      request<PurchaseOrderSummary[]>(
        `/api/purchase-orders?vendorId=${encodeURIComponent(vendorId)}`,
      ),

    lines: (poNum: number) =>
      request<PurchaseOrderDetailLine[]>(`/api/purchase-orders/${poNum}/lines`),

    reportUrl: (poNum: number) => `/api/purchase-orders/${poNum}/report`,

    sendCopy: (poNum: number) =>
      request<{ message: string }>(`/api/purchase-orders/${poNum}/send-copy`, {
        method: 'POST',
      }),

    emailRecipients: (vendorId: string) =>
      request<EmailRecipient[]>(
        `/api/purchase-orders/email-recipients?vendorId=${encodeURIComponent(vendorId)}`,
      ),

    sendToVendor: (poNum: number, vendorId: string, manualEmails: string[]) =>
      request<void>(`/api/purchase-orders/${poNum}/send-to-vendor`, {
        method: 'POST',
        body: JSON.stringify({ vendorId, manualEmails }),
      }),
  },
}
