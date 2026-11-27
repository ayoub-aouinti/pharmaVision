export type Period = 'live' | 'today' | '7d' | '30d'
export type ZoneType = 'Shelf' | 'Passage' | 'Queue' | 'CountingLine' | 'Restricted' | 'PrivacyMask'
export type CameraStatus = 'Online' | 'Offline' | 'Unconfigured'
export type AlertKind = 'LowFill' | 'QueueWait' | 'Unattended' | 'RestrictedPresence' | 'CameraOffline'
export type AlertStatus = 'Open' | 'Acknowledged' | 'Resolved'
export type Channel = 'Dashboard' | 'Mobile' | 'WhatsApp'
export type AlertAction = 'restock' | 'notify' | 'dispatch' | 'diagnose' | 'resolve'

export interface Pharmacy {
  id: number
  name: string
  city: string
  group: string | null
  timeZone: string
  device: { name: string; version: string; lastSeen: string | null; online: boolean } | null
  activeAlerts: number
}

export interface Kpi {
  key: string
  label: string
  value: number
  unit: 'count' | 'percent' | 'seconds'
  trend: number | null
  higherIsBetter: boolean
}

export interface KpiSummary {
  from: string
  to: string
  occupancy: number
  items: Kpi[]
}

export interface ShelfRow {
  zoneId: number
  name: string
  category: string | null
  brands: string | null
  cameraName: string
  fill: number | null
  state: 'Conforme' | 'StockBas' | 'Rupture' | 'Inconnu'
  stops: number
  engagementRate: number
  pickupRate: number
  transformation: number
  ruptureMinutes: number
}

export interface HeatCell {
  zoneId: number
  name: string
  type: ZoneType
  x: number | null
  y: number | null
  w: number | null
  h: number | null
  dwellSeconds: number
  intensity: number
}

export interface HourPoint {
  hour: number
  visitors: number
  tickets: number
}

export interface Alert {
  id: number
  zoneId: number | null
  cameraId: number | null
  kind: AlertKind
  severity: 'Medium' | 'High'
  title: string
  detail: string
  startedAt: string
  status: AlertStatus
  handledBy: string | null
  actionTaken: AlertAction | null
}

export interface Point {
  x: number
  y: number
}

export interface Rule {
  kind: AlertKind
  threshold: number
  durationSec: number
  channel: Channel
  enabled: boolean
}

export interface Zone {
  /** 0 tant que la zone n'est pas enregistrée. */
  id: number
  cameraId: number
  name: string
  type: ZoneType
  polygon: Point[]
  category: string | null
  brands: string | null
  planogramRef: string | null
  facingsExpected: number | null
  rules: Rule[] | null
}

export interface Camera {
  id: number
  name: string
  brand: string
  protocol: string
  rtspUrl: string
  resolution: string
  status: CameraStatus
  referencePhotoAt: string | null
  peopleNow: number
  zones: Zone[]
}

export interface CameraInput {
  pharmacyId: number
  name: string
  brand: string
  rtspUrl: string
  resolution?: string
  status: CameraStatus
}

export interface DiscoveredCamera {
  name: string
  brand: string
  rtspUrl: string
  resolution: string
}
