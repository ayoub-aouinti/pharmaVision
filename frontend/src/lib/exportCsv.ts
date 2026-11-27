import type { Alert, HourPoint, KpiSummary, Period, Pharmacy, ProductReport, ShelfRow } from '../api/types'
import { ACTION_DONE, ALERT_KINDS, STATUS_LABELS } from '../features/alerts/shared'

// Séparateur « ; » et virgule décimale : ouverture directe dans Excel en français.
function cell(value: string | number | null) {
  if (value === null) return ''
  if (typeof value === 'number') return String(Math.round(value * 1000) / 1000).replace('.', ',')
  // Un texte saisi commençant par = + - @ serait interprété comme une formule.
  const safe = /^[=+\-@]/.test(value) ? `'${value}` : value
  return `"${safe.replaceAll('"', '""')}"`
}

export function downloadReport(
  pharmacy: Pharmacy, period: Period, kpis: KpiSummary, shelves: ShelfRow[], hourly: HourPoint[],
) {
  const rows: (string | number | null)[][] = [
    ['PharmaVision (données fictives)', pharmacy.name, pharmacy.city],
    ['Période', period, kpis.from, kpis.to],
    [],
    ['Indicateur', 'Valeur', 'Unité', 'Tendance'],
    ...kpis.items.map((k) => [k.label, k.value, k.unit, k.trend]),
    [],
    ['Linéaire', 'Remplissage %', 'État', 'Arrêts', 'Engagement', 'Prise en main', 'Transformation', 'Rupture (min)'],
    ...shelves.map((s) => [s.name, s.fill, s.state, s.stops, s.engagementRate, s.pickupRate, s.transformation, s.ruptureMinutes]),
    [],
    ['Heure', 'Visiteurs', 'Tickets'],
    ...hourly.map((h) => [h.hour, h.visitors, h.tickets]),
  ]
  save(`pharmavision-${pharmacy.city.toLowerCase()}-${period}-${kpis.to.slice(0, 10)}.csv`, rows)
}

export function downloadAlerts(pharmacy: Pharmacy, period: Period, alerts: Alert[]) {
  const date = (iso: string | null) =>
    iso && new Date(iso).toLocaleString('fr-FR', { timeZone: pharmacy.timeZone })
  save(`pharmavision-alertes-${pharmacy.city.toLowerCase()}-${period}.csv`, [
    ['PharmaVision (données fictives)', pharmacy.name, pharmacy.city],
    [],
    ['Début', 'Type', 'Alerte', 'Détail', 'Priorité', 'État', 'Action', 'Pris en charge le', 'Par', 'Fin', 'Clôturée par'],
    ...alerts.map((a) => [
      date(a.startedAt), ALERT_KINDS[a.kind].label, a.title, a.detail,
      a.severity === 'High' ? 'Forte' : 'Moyenne', STATUS_LABELS[a.status],
      a.actionTaken && ACTION_DONE[a.actionTaken], date(a.acknowledgedAt), a.handledBy, date(a.endedAt),
      a.status === 'Resolved' ? a.resolvedBy ?? 'Automatique' : null,
    ]),
  ])
}

export function downloadProducts(pharmacy: Pharmacy, period: Period, report: ProductReport) {
  save(`pharmavision-marques-${pharmacy.city.toLowerCase()}-${period}-${report.to.slice(0, 10)}.csv`, [
    ['PharmaVision (données fictives)', pharmacy.name, pharmacy.city],
    ['Période', period, report.from, report.to],
    [],
    ['Famille', 'Unités', 'Chiffre d’affaires (TND)', 'Tendance', 'Tickets', 'Arrêts', 'Prises en main', 'Transformation'],
    ...report.families.map((f) => [
      f.family, f.units, f.revenue, f.revenueTrend, f.tickets,
      f.hasShelf ? f.stops : null, f.hasShelf ? f.pickups : null, f.transformation,
    ]),
    [],
    ['Marque', 'Familles', 'Unités', 'Chiffre d’affaires (TND)', 'Tendance', 'Part', 'Linéaires', 'Arrêts des linéaires (partagés)'],
    ...report.brands.map((b) => [
      b.brand, b.families.join(', '), b.units, b.revenue, b.revenueTrend, b.revenueShare, b.shelves.join(', '),
      b.shelves.length ? b.shelfStops : null,
    ]),
    [],
    ['Code', 'Produit', 'Marque', 'Famille', 'Unités', 'Chiffre d’affaires (TND)'],
    ...report.topProducts.map((p) => [p.code, p.name, p.brand, p.family, p.units, p.revenue]),
  ])
}

function save(filename: string, rows: (string | number | null)[][]) {
  const csv = '﻿' + rows.map((r) => r.map(cell).join(';')).join('\r\n')
  const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }))
  const link = document.createElement('a')
  link.href = url
  link.download = filename
  link.click()
  URL.revokeObjectURL(url)
}
