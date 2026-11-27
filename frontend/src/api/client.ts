import type {
  Alert, AlertAction, AlertContext, AlertFilters, AlertPage, Camera, CameraInput, CurrentUser, DayReplay, Device, DeviceKey, DiscoveredCamera, HeatCell,
  HourPoint, KpiSummary, Period, Pharmacy, ProductReport, ReportFormat, ReportKind, ShelfRow, Zone, ZoneDetail, ZoneRow,
  NewUser, PharmacySettings, User, UserChanges,
} from './types'

export class ApiError extends Error {
  constructor(message: string, readonly status: number) {
    super(message)
  }
}

async function send(path: string, method = 'GET', body?: unknown) {
  const response = await fetch(`/api${path}`, {
    method,
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (!response.ok) {
    throw new ApiError(errorMessage(await response.text()) || `Erreur ${response.status}`, response.status)
  }
  return response
}

async function request<T>(path: string, method = 'GET', body?: unknown): Promise<T> {
  const text = await (await send(path, method, body)).text()
  return (text ? JSON.parse(text) : undefined) as T
}

/** Fichier produit par le serveur, avec le nom qu'il propose. */
async function file(path: string) {
  const response = await send(path)
  const filename = /filename=("?)([^";]+)\1/.exec(response.headers.get('Content-Disposition') ?? '')?.[2]
  return { filename: filename ?? 'rapport', blob: await response.blob() }
}

/** Message lisible à partir d'une réponse d'erreur ASP.NET Core. */
function errorMessage(text: string) {
  try {
    const problem = JSON.parse(text)
    if (typeof problem === 'string') return problem
    return Object.values(problem.errors ?? {}).flat().join(' ') || problem.title
  } catch {
    return text
  }
}

const scoped = (path: string, pharmacyId: number, period: Period) =>
  `/dashboard/${path}?pharmacyId=${pharmacyId}&period=${period}`

export const api = {
  /** Null quand aucune session n'est ouverte. */
  me: () => request<CurrentUser>('/auth/me').catch((error) => {
    if (error instanceof ApiError && error.status === 401) return null
    throw error
  }),
  login: (email: string, password: string) => request<CurrentUser>('/auth/login', 'POST', { email, password }),
  logout: () => request<void>('/auth/logout', 'POST'),
  changePassword: (currentPassword: string, newPassword: string) =>
    request<void>('/auth/password', 'POST', { currentPassword, newPassword }),
  users: () => request<User[]>('/users'),
  createUser: (user: NewUser) => request<User>('/users', 'POST', user),
  updateUser: (id: number, changes: UserChanges) => request<User>(`/users/${id}`, 'PUT', changes),
  resetPassword: (id: number, password: string) => request<void>(`/users/${id}/password`, 'POST', { password }),
  deleteUser: (id: number) => request<void>(`/users/${id}`, 'DELETE'),
  updatePharmacy: (id: number, settings: PharmacySettings) => request<void>(`/pharmacies/${id}`, 'PUT', settings),
  devices: (pharmacyId: number) => request<Device[]>(`/devices?pharmacyId=${pharmacyId}`),
  addDevice: (pharmacyId: number, name: string) => request<DeviceKey>('/devices', 'POST', { pharmacyId, name }),
  renewDeviceKey: (id: number) => request<DeviceKey>(`/devices/${id}/key`, 'POST'),
  deleteDevice: (id: number) => request<void>(`/devices/${id}`, 'DELETE'),
  alertHistory: (id: number, period: Period, filters: AlertFilters, page: number, pageSize = 20) => {
    const query = new URLSearchParams({
      pharmacyId: String(id), period, state: filters.state, page: String(page), pageSize: String(pageSize),
    })
    if (filters.kind) query.set('kind', filters.kind)
    if (filters.severity) query.set('severity', filters.severity)
    return request<AlertPage>(`/alerts/history?${query}`)
  },
  pharmacies: () => request<Pharmacy[]>('/pharmacies'),
  kpis: (id: number, p: Period) => request<KpiSummary>(scoped('kpis', id, p)),
  shelves: (id: number, p: Period) => request<ShelfRow[]>(scoped('shelves', id, p)),
  heatmap: (id: number, p: Period) => request<HeatCell[]>(scoped('heatmap', id, p)),
  hourly: (id: number, p: Period) => request<HourPoint[]>(scoped('hourly', id, p)),
  zoneRows: (id: number, p: Period) => request<ZoneRow[]>(`/analytics/zones?pharmacyId=${id}&period=${p}`),
  zoneDetail: (id: number, zoneId: number, p: Period) =>
    request<ZoneDetail>(`/analytics/zones/${zoneId}?pharmacyId=${id}&period=${p}`),
  productReport: (id: number, p: Period) => request<ProductReport>(`/analytics/products?pharmacyId=${id}&period=${p}`),
  brands: () => request<string[]>('/reports/brands'),
  report: (kind: ReportKind, id: number, period: Period, format: ReportFormat, brand?: string) => {
    const query = new URLSearchParams({ pharmacyId: String(id), period, format })
    if (brand) query.set('brand', brand)
    return file(`/reports/${kind}?${query}`)
  },
  historyDay: (id: number, date: string) =>
    request<DayReplay>(`/history/day?pharmacyId=${id}${date && `&date=${date}`}`),
  alertContext: (id: number) => request<AlertContext>(`/alerts/${id}/context`),
  alerts: (id: number) => request<Alert[]>(`/alerts?pharmacyId=${id}&limit=100`),
  alertAction: (id: number, action: AlertAction) => request<Alert>(`/alerts/${id}/action`, 'POST', { action }),
  cameras: (id: number) => request<Camera[]>(`/cameras?pharmacyId=${id}`),
  createCamera: (input: CameraInput) => request<number>('/cameras', 'POST', input),
  updateCamera: (id: number, input: CameraInput) => request<void>(`/cameras/${id}`, 'PUT', input),
  deleteCamera: (id: number) => request<void>(`/cameras/${id}`, 'DELETE'),
  discover: (id: number) => request<DiscoveredCamera[]>(`/cameras/discover?pharmacyId=${id}`, 'POST'),
  referencePhoto: (id: number) => request<void>(`/cameras/${id}/reference-photo`, 'POST'),
  saveZone: (zone: Zone) =>
    zone.id ? request<Zone>(`/zones/${zone.id}`, 'PUT', zone) : request<Zone>('/zones', 'POST', zone),
  deleteZone: (id: number) => request<void>(`/zones/${id}`, 'DELETE'),
}

/** La date de la photo de référence dans l'adresse force le rechargement d'une nouvelle image. */
export const snapshotUrl = (cameraId: number, version?: string | null) =>
  `/api/cameras/${cameraId}/snapshot${version ? `?v=${encodeURIComponent(version)}` : ''}`
export const thumbnailUrl = (alertId: number) => `/api/alerts/${alertId}/thumbnail`
