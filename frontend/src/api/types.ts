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
  thumbnailDays: number
  statisticsMonths: number
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

export type ShelfState = ShelfRow['state']

export interface ZoneRow {
  zoneId: number
  name: string
  type: ZoneType
  cameraId: number
  cameraName: string
  cameraStatus: CameraStatus
  category: string | null
  brands: string | null
  planogramRef: string | null
  facingsExpected: number | null
  rules: AlertKind[]
  visits: number
  stops: number
  pickups: number
  fill: number | null
  state: ShelfState | null
  avgWaitSeconds: number | null
  activeAlerts: number
}

export interface ZonePointStats {
  start: string
  visits: number
  stops: number
  pickups: number
  level: number | null
  minLevel: number | null
  waitSeconds: number | null
}

export interface ZoneDetail {
  zoneId: number
  name: string
  type: ZoneType
  bucketMinutes: number
  fillThreshold: number | null
  ruptureMinutes: number
  avgStopSeconds: number
  points: ZonePointStats[]
  alertCount: number
  alerts: Alert[]
}

export interface FamilyRow {
  family: string
  units: number
  revenue: number
  tickets: number
  revenueTrend: number | null
  hasShelf: boolean
  stops: number
  pickups: number
  transformation: number | null
}

export interface BrandRow {
  brand: string
  families: string[]
  units: number
  revenue: number
  revenueTrend: number | null
  revenueShare: number
  shelves: string[]
  shelfStops: number
}

export interface ProductRow {
  code: string
  name: string
  brand: string
  family: string
  units: number
  revenue: number
}

export interface ProductReport {
  from: string
  to: string
  revenue: number
  units: number
  tickets: number
  unreferencedRevenue: number
  families: FamilyRow[]
  brands: BrandRow[]
  topProducts: ProductRow[]
}

export type ReportKind = 'synthese' | 'zones' | 'produits' | 'laboratoire'
export type ReportFormat = 'pdf' | 'xlsx'

export interface ReplayZone {
  id: number
  name: string
  type: ZoneType
  x: number | null
  y: number | null
  w: number | null
  h: number | null
  threshold: number | null
}

export interface ReplayFrame {
  start: string
  entries: number
  exits: number
  occupancy: number
  tickets: number
  waitSeconds: number | null
  zones: { zoneId: number; level: number | null; presence: number }[]
}

export interface DayReplay {
  date: string
  firstDay: string
  today: string
  stepMinutes: number
  zones: ReplayZone[]
  frames: ReplayFrame[]
  alerts: Alert[]
}

export interface AlertContext {
  metric: string
  unit: 'percent' | 'seconds'
  threshold: number | null
  bucketMinutes: number
  points: { start: string; value: number | null }[]
}

export interface ImportError {
  line: number
  message: string
}

export interface SalesImportReport {
  importId: number | null
  rows: number
  newRows: number
  duplicates: number
  tickets: number
  revenue: number
  from: string | null
  to: string | null
  replaced: number
  unknownCodes: number
  unknownExamples: string[]
  familiesWithoutShelf: string[]
  errorCount: number
  errors: ImportError[]
}

export interface CatalogImportReport {
  applied: boolean
  rows: number
  added: number
  updated: number
  errorCount: number
  errors: ImportError[]
}

export interface SalesImport {
  id: number
  fileName: string
  importedAt: string
  importedBy: string
  rows: number
  firstDay: string
  lastDay: string
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
  endedAt: string | null
  handledBy: string | null
  actionTaken: AlertAction | null
  acknowledgedAt: string | null
  /** Null pour une clôture automatique. */
  resolvedBy: string | null
}

export interface AlertStats {
  total: number
  active: number
  resolved: number
  autoResolved: number
  avgAcknowledgeSeconds: number | null
  avgResolutionSeconds: number | null
}

export interface AlertPage {
  items: Alert[]
  total: number
  page: number
  pageSize: number
  stats: AlertStats
}

export interface AlertFilters {
  state: 'all' | 'active' | 'resolved'
  kind: AlertKind | ''
  severity: Alert['severity'] | ''
}

export type Role = 'Titulaire' | 'Equipe'

export interface CurrentUser {
  id: number
  name: string
  email: string
  role: Role
}

export interface User {
  id: number
  email: string
  displayName: string
  role: Role
  pharmacyIds: number[]
  isActive: boolean
  locked: boolean
}

export type UserChanges = Pick<User, 'displayName' | 'role' | 'pharmacyIds' | 'isActive'>
export type NewUser = Pick<User, 'email' | 'displayName' | 'role' | 'pharmacyIds'> & { password: string }
export type PharmacySettings = Pick<Pharmacy, 'name' | 'city' | 'thumbnailDays' | 'statisticsMonths'>

export interface Device {
  id: number
  name: string
  version: string
  lastSeen: string | null
  cameras: number
}

export interface DeviceKey {
  deviceId: number
  key: string
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
  deviceId: number
  /** Vrai quand un boîtier a envoyé une image réelle. */
  hasImage: boolean
}

export interface CameraInput {
  pharmacyId: number
  name: string
  brand: string
  rtspUrl: string
  resolution?: string
  status: CameraStatus
  deviceId?: number
}

export interface DiscoveredCamera {
  name: string
  brand: string
  rtspUrl: string
  resolution: string
}
